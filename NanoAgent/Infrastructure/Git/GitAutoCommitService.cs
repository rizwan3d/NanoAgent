using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Infrastructure.Storage;
using System.Text;

namespace NanoAgent.Infrastructure.Git;

internal sealed class GitAutoCommitService : IAutoCommitService
{
    private const int MaxCommitSuggestionSeconds = 20;
    private const string SystemPrompt =
        """
        You write git commit subjects for coding changes.
        Return exactly one line with no quotes, bullets, code fences, or explanation.
        Keep it concise, imperative, and prefer a conventional commit prefix when it clearly fits.
        """;

    private readonly IApiKeySecretStore _secretStore;
    private readonly IConversationProviderClient _providerClient;
    private readonly IConversationResponseMapper _responseMapper;
    private readonly IConversationConfigurationAccessor _configurationAccessor;
    private readonly IProcessRunner _processRunner;
    public GitAutoCommitService(
        IApiKeySecretStore secretStore,
        IConversationProviderClient providerClient,
        IConversationResponseMapper responseMapper,
        IConversationConfigurationAccessor configurationAccessor,
        IProcessRunner processRunner)
    {
        _secretStore = secretStore;
        _providerClient = providerClient;
        _responseMapper = responseMapper;
        _configurationAccessor = configurationAccessor;
        _processRunner = processRunner;
    }

    public async Task TryAutoCommitAsync(
        ReplSessionContext session,
        IReadOnlyList<SessionEditContext> newEdits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(newEdits);

        if (newEdits.Count == 0)
        {
            return;
        }

        string workspacePath = session.WorkspacePath;
        if (!AgentProfileConfigurationReader.LoadWorkspaceGitAutomationSettings(workspacePath).AutoCommitAfterAiChanges)
        {
            return;
        }

        if (!await IsGitRepositoryAsync(workspacePath, cancellationToken))
        {
            return;
        }

        string[] changedPaths = CollectChangedPaths(newEdits);
        if (changedPaths.Length == 0)
        {
            return;
        }

        string repositoryRoot = await GetRepositoryRootAsync(workspacePath, cancellationToken);
        await StagePathsAsync(repositoryRoot, changedPaths, cancellationToken);

        if (!await HasStagedChangesAsync(repositoryRoot, cancellationToken))
        {
            return;
        }

        string message = await BuildCommitMessageAsync(session, repositoryRoot, cancellationToken);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = "chore: apply NanoAgent changes";
        }

        await RunGitAsync(repositoryRoot, ["commit", "-m", message], cancellationToken);
    }

    private async Task<string> BuildCommitMessageAsync(
        ReplSessionContext session,
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        string? apiKey = await LoadProviderSecretAsync(session, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "chore: apply NanoAgent changes";
        }

        string stagedFiles = await GetGitOutputAsync(
            repositoryRoot,
            ["diff", "--cached", "--name-only"],
            cancellationToken);
        string diffSummary = await BuildCommitMessageDiffSummaryAsync(repositoryRoot, cancellationToken);
        string prompt = BuildCommitMessageSuggestionPrompt(stagedFiles, diffSummary);

        ConversationSettings settings = _configurationAccessor.GetSettings();
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(GetTimeout(settings.RequestTimeout));

        ConversationProviderPayload payload = await _providerClient.SendAsync(
            new ConversationProviderRequest(
                session.ProviderProfile,
                apiKey,
                session.ActiveModelId,
                [ConversationRequestMessage.User(prompt)],
                SystemPrompt,
                AvailableTools: [],
                session.ReasoningEffort,
                ThinkingMode: session.ThinkingMode,
                ShowThinking: false),
            timeoutSource.Token);

        ConversationResponse response = _responseMapper.Map(payload);
        return SanitizeCommitMessageSuggestion(response.AssistantMessage);
    }

    private async Task<string?> LoadProviderSecretAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(session.ActiveProviderName))
        {
            string? providerSecret = await _secretStore.LoadAsync(
                session.ActiveProviderName,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(providerSecret))
            {
                return providerSecret;
            }
        }

        return await _secretStore.LoadAsync(cancellationToken);
    }

    private async Task<bool> IsGitRepositoryAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner.RunAsync(
            new ProcessExecutionRequest(
                "git",
                ["rev-parse", "--is-inside-work-tree"],
                WorkingDirectory: workingDirectory,
                MaxOutputCharacters: 256),
            cancellationToken);

        return result.ExitCode == 0 &&
               string.Equals(result.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await RunGitAsync(
            workingDirectory,
            ["rev-parse", "--show-toplevel"],
            cancellationToken);
        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? workingDirectory
            : result.StandardOutput.Trim();
    }

    private async Task StagePathsAsync(
        string repositoryRoot,
        IReadOnlyList<string> changedPaths,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ["add", "-A", "--"];
        arguments.AddRange(changedPaths);
        await RunGitAsync(repositoryRoot, arguments, cancellationToken);
    }

    private async Task<bool> HasStagedChangesAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner.RunAsync(
            new ProcessExecutionRequest(
                "git",
                ["diff", "--cached", "--quiet"],
                WorkingDirectory: repositoryRoot,
                MaxOutputCharacters: 0),
            cancellationToken);

        return result.ExitCode == 1;
    }

    private async Task<string> BuildCommitMessageDiffSummaryAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        string stats = await GetGitOutputAsync(
            repositoryRoot,
            ["diff", "--cached", "--stat", "--summary", "--find-renames"],
            cancellationToken);
        string patch = await GetGitOutputAsync(
            repositoryRoot,
            ["diff", "--cached", "--unified=0", "--no-ext-diff", "--no-color", "--minimal"],
            cancellationToken);

        string combined = string.IsNullOrWhiteSpace(patch)
            ? stats
            : string.IsNullOrWhiteSpace(stats)
                ? patch
                : stats.TrimEnd() + Environment.NewLine + Environment.NewLine + patch.TrimStart();

        string normalized = combined
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();

        if (normalized.Length == 0)
        {
            return "(no diff details available)";
        }

        const int maxLength = 12000;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + Environment.NewLine + "[truncated]";
    }

    private async Task<string> GetGitOutputAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await RunGitAsync(repositoryRoot, arguments, cancellationToken);
        return result.StandardOutput ?? string.Empty;
    }

    private async Task<ProcessExecutionResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner.RunAsync(
            new ProcessExecutionRequest(
                "git",
                arguments,
                WorkingDirectory: workingDirectory,
                MaxOutputCharacters: 20000),
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"git {string.Join(' ', arguments)} failed with exit code {result.ExitCode}."
                    : result.StandardError.Trim());
        }

        return result;
    }

    private static string[] CollectChangedPaths(IReadOnlyList<SessionEditContext> edits)
    {
        return edits
            .SelectMany(static edit => edit.Paths ?? [])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path =>
            {
                string trimmed = path.Trim();
                return trimmed.Contains("->", StringComparison.Ordinal)
                    ? trimmed[(trimmed.LastIndexOf("->", StringComparison.Ordinal) + 2)..].Trim()
                    : trimmed;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildCommitMessageSuggestionPrompt(string stagedFiles, string diffSummary)
    {
        return
            "Write a git commit message for the staged changes below." + Environment.NewLine +
            "Return exactly one line: no bullets, no quotes, no code fences, and no explanation." + Environment.NewLine +
            "Use imperative mood and keep it concise. Prefer a conventional-commit prefix when it clearly fits." + Environment.NewLine +
            Environment.NewLine +
            "Staged files:" + Environment.NewLine +
            stagedFiles.Trim() + Environment.NewLine +
            Environment.NewLine +
            "Staged diff summary:" + Environment.NewLine +
            diffSummary;
    }

    private static string SanitizeCommitMessageSuggestion(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        string[] lines = responseText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        string suggestion = string.Empty;
        foreach (string line in lines)
        {
            string candidate = line.Trim();
            if (candidate.StartsWith("```", StringComparison.Ordinal) ||
                candidate == "```")
            {
                continue;
            }

            candidate = candidate
                .Trim('`', '"', '\'')
                .TrimStart('-', '*', ' ');

            if (candidate.Length == 0)
            {
                continue;
            }

            suggestion = candidate;
            break;
        }

        if (suggestion.Length == 0)
        {
            return string.Empty;
        }

        return suggestion.Length <= 72
            ? suggestion
            : suggestion[..72].TrimEnd();
    }

    private static TimeSpan GetTimeout(TimeSpan conversationTimeout)
    {
        TimeSpan cap = TimeSpan.FromSeconds(MaxCommitSuggestionSeconds);
        if (conversationTimeout <= TimeSpan.Zero)
        {
            return cap;
        }

        return conversationTimeout < cap
            ? conversationTimeout
            : cap;
    }
}
