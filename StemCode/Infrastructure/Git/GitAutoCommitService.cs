using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Application.Utilities;
using StemCode.Infrastructure.Secrets;
using StemCode.Infrastructure.Storage;
using System.Security.Cryptography;
using System.Text;

namespace StemCode.Infrastructure.Git;

internal sealed class GitAutoCommitService : IAutoCommitService
{
    private sealed record GitRepositoryState(
        string? HeadSha,
        string? BranchReference);

    private const int MaxCommitSuggestionSeconds = 20;
    private const string AutoCommitFallbackMessage = "chore: apply StemCode changes";
    private const string AutoCommitCoAuthorTrailer = "Co-authored-by: StemCodeAi <313132566+StemCodeAi@users.noreply.github.com>";
    private const string GitIndexFileEnvironmentVariable = "GIT_INDEX_FILE";
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

        string repositoryRoot = await GetRepositoryRootAsync(workspacePath, cancellationToken);
        string[] changedPaths = ConvertToRepositoryRelativePaths(
            workspacePath,
            repositoryRoot,
            CollectChangedPaths(newEdits));
        if (changedPaths.Length == 0)
        {
            return;
        }

        string[] stagedPaths = await GetStagedPathsAsync(repositoryRoot, cancellationToken);
        if (stagedPaths.Length > 0)
        {
            return;
        }

        string initialIndexTree = await GetIndexTreeAsync(repositoryRoot, cancellationToken);
        GitRepositoryState initialRepositoryState = await GetRepositoryStateAsync(repositoryRoot, cancellationToken);

        string temporaryIndexPath = Path.Combine(
            Path.GetTempPath(),
            "stemcode-autocommit-index-" + Guid.NewGuid().ToString("N"));

        try
        {
            IReadOnlyDictionary<string, string> gitEnvironment =
                CreateGitEnvironmentVariables(temporaryIndexPath);

            if (await HasHeadCommitAsync(repositoryRoot, cancellationToken))
            {
                await RunGitAsync(
                    repositoryRoot,
                    ["read-tree", "HEAD"],
                    cancellationToken,
                    gitEnvironment);
            }

            await StagePathsAsync(repositoryRoot, changedPaths, cancellationToken, gitEnvironment);

            if (!await HasStagedChangesAsync(repositoryRoot, cancellationToken, gitEnvironment))
            {
                return;
            }

            if (!await IsRepositoryStateUnchangedAsync(
                    repositoryRoot,
                    initialRepositoryState,
                    cancellationToken) ||
                !await IsIndexTreeUnchangedAsync(
                    repositoryRoot,
                    initialIndexTree,
                    cancellationToken) ||
                !TryHasMatchingTrackedFileStates(session, changedPaths))
            {
                return;
            }

            string message = await BuildCommitMessageAsync(session, repositoryRoot, cancellationToken, gitEnvironment);
            if (string.IsNullOrWhiteSpace(message))
            {
                message = AutoCommitFallbackMessage;
            }

            await RunGitAsync(
                repositoryRoot,
                ["commit", "-m", message, "-m", AutoCommitCoAuthorTrailer],
                cancellationToken,
                gitEnvironment);

            if (await IsIndexTreeUnchangedAsync(repositoryRoot, initialIndexTree, cancellationToken))
            {
                await RunGitAsync(
                    repositoryRoot,
                    ["reset", "--mixed", "HEAD"],
                    cancellationToken);
            }
        }
        finally
        {
            TryDeleteTemporaryIndex(temporaryIndexPath);
        }
    }

    private async Task<string> BuildCommitMessageAsync(
        ReplSessionContext session,
        string repositoryRoot,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        string? apiKey = await LoadProviderSecretAsync(session, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AutoCommitFallbackMessage;
        }

        string stagedFiles = await GetGitOutputAsync(
            repositoryRoot,
            ["diff", "--cached", "--name-only"],
            cancellationToken,
            environmentVariables);
        string diffSummary = await BuildCommitMessageDiffSummaryAsync(
            repositoryRoot,
            cancellationToken,
            environmentVariables);
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
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        List<string> arguments = ["add", "-A", "--"];
        arguments.AddRange(changedPaths);
        await RunGitAsync(repositoryRoot, arguments, cancellationToken, environmentVariables);
    }

    private async Task<bool> HasStagedChangesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        ProcessExecutionResult result = await _processRunner.RunAsync(
            new ProcessExecutionRequest(
                "git",
                ["diff", "--cached", "--quiet"],
                WorkingDirectory: repositoryRoot,
                MaxOutputCharacters: 0,
                EnvironmentVariables: environmentVariables),
            cancellationToken);

        return result.ExitCode == 1;
    }

    private async Task<string> BuildCommitMessageDiffSummaryAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        string stats = await GetGitOutputAsync(
            repositoryRoot,
            ["diff", "--cached", "--stat", "--summary", "--find-renames"],
            cancellationToken,
            environmentVariables);
        string patch = await GetGitOutputAsync(
            repositoryRoot,
            ["diff", "--cached", "--unified=0", "--no-ext-diff", "--no-color", "--minimal"],
            cancellationToken,
            environmentVariables);

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
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        ProcessExecutionResult result = await RunGitAsync(
            repositoryRoot,
            arguments,
            cancellationToken,
            environmentVariables);
        return result.StandardOutput ?? string.Empty;
    }

    private async Task<ProcessExecutionResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        ProcessExecutionResult result = await _processRunner.RunAsync(
            new ProcessExecutionRequest(
                "git",
                arguments,
                WorkingDirectory: workingDirectory,
                MaxOutputCharacters: 20000,
                EnvironmentVariables: environmentVariables),
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
            .SelectMany(static path =>
            {
                string trimmed = path.Trim();
                if (!trimmed.Contains("->", StringComparison.Ordinal))
                {
                    return [trimmed];
                }

                int renameSeparatorIndex = trimmed.LastIndexOf("->", StringComparison.Ordinal);
                string sourcePath = trimmed[..renameSeparatorIndex].Trim();
                string destinationPath = trimmed[(renameSeparatorIndex + 2)..].Trim();

                return new[] { sourcePath, destinationPath }
                    .Where(static candidate => !string.IsNullOrWhiteSpace(candidate));
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ConvertToRepositoryRelativePaths(
        string workspacePath,
        string repositoryRoot,
        IReadOnlyList<string> changedPaths)
    {
        return changedPaths
            .Select(path => WorkspacePath.Resolve(workspacePath, path))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<bool> HasHeadCommitAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner.RunAsync(
            new ProcessExecutionRequest(
                "git",
                ["rev-parse", "--verify", "HEAD"],
                WorkingDirectory: repositoryRoot,
                MaxOutputCharacters: 256),
            cancellationToken);

        return result.ExitCode == 0;
    }

    private async Task<string[]> GetStagedPathsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        string output = await GetGitOutputAsync(
            repositoryRoot,
            ["diff", "--cached", "--name-only"],
            cancellationToken);

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string> GetIndexTreeAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await RunGitAsync(
            repositoryRoot,
            ["write-tree"],
            cancellationToken);

        return result.StandardOutput.Trim();
    }

    private async Task<GitRepositoryState> GetRepositoryStateAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        string? headSha = await TryGetGitOutputAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "HEAD"],
            cancellationToken);
        string? branchReference = await TryGetGitOutputAsync(
            repositoryRoot,
            ["symbolic-ref", "-q", "HEAD"],
            cancellationToken);

        return new GitRepositoryState(
            NormalizeGitOutput(headSha),
            NormalizeGitOutput(branchReference));
    }

    private async Task<string?> TryGetGitOutputAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner.RunAsync(
            new ProcessExecutionRequest(
                "git",
                arguments,
                WorkingDirectory: repositoryRoot,
                MaxOutputCharacters: 256),
            cancellationToken);

        return result.ExitCode == 0
            ? result.StandardOutput
            : null;
    }

    private async Task<bool> IsRepositoryStateUnchangedAsync(
        string repositoryRoot,
        GitRepositoryState expectedState,
        CancellationToken cancellationToken)
    {
        GitRepositoryState currentState = await GetRepositoryStateAsync(repositoryRoot, cancellationToken);
        return string.Equals(currentState.HeadSha, expectedState.HeadSha, StringComparison.Ordinal) &&
               string.Equals(currentState.BranchReference, expectedState.BranchReference, StringComparison.Ordinal);
    }

    private async Task<bool> IsIndexTreeUnchangedAsync(
        string repositoryRoot,
        string expectedIndexTree,
        CancellationToken cancellationToken)
    {
        string currentIndexTree = await GetIndexTreeAsync(repositoryRoot, cancellationToken);
        return string.Equals(currentIndexTree, expectedIndexTree, StringComparison.Ordinal);
    }

    private static string? NormalizeGitOutput(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static bool TryHasMatchingTrackedFileStates(
        ReplSessionContext session,
        IReadOnlyList<string> changedPaths)
    {
        if (!session.TryCreateFileEditTransactionSnapshot(
                "auto-commit snapshot",
                out WorkspaceFileEditTransaction? transaction) ||
            transaction is null)
        {
            return true;
        }

        StringComparer pathComparer = WorkspacePath.GetPathComparer();
        HashSet<string> changedPathSet = new(
            changedPaths
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => path.Trim()),
            pathComparer);
        HashSet<string> trackedPaths = new(
            transaction.BeforeStates.Select(static state => state.Path)
                .Concat(transaction.AfterStates.Select(static state => state.Path))
                .Where(static path => !string.IsNullOrWhiteSpace(path)),
            pathComparer);

        if (!changedPathSet.IsSubsetOf(trackedPaths))
        {
            return true;
        }

        Dictionary<string, WorkspaceFileEditState> afterStatesByPath = transaction.AfterStates
            .Where(static state => !string.IsNullOrWhiteSpace(state.Path))
            .GroupBy(static state => state.Path, pathComparer)
            .ToDictionary(static group => group.Key, static group => group.Last(), pathComparer);

        foreach (string changedPath in changedPathSet)
        {
            if (!afterStatesByPath.TryGetValue(changedPath, out WorkspaceFileEditState? expectedState))
            {
                if (File.Exists(WorkspacePath.Resolve(session.WorkspacePath, changedPath)))
                {
                    return false;
                }

                continue;
            }

            if (!MatchesWorkspaceFileState(session.WorkspacePath, expectedState))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesWorkspaceFileState(
        string workspacePath,
        WorkspaceFileEditState expectedState)
    {
        string fullPath = WorkspacePath.Resolve(workspacePath, expectedState.Path);
        bool fileExists = File.Exists(fullPath);

        if (!expectedState.Exists)
        {
            return !fileExists;
        }

        if (!fileExists)
        {
            return false;
        }

        if (expectedState.Content is not null)
        {
            string currentContent = File.ReadAllText(fullPath);
            return string.Equals(currentContent, expectedState.Content, StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(expectedState.ContentHash))
        {
            return true;
        }

        using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        string currentHash = Convert.ToHexStringLower(SHA256.HashData(stream));
        return string.Equals(currentHash, expectedState.ContentHash, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> CreateGitEnvironmentVariables(string indexPath)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GitIndexFileEnvironmentVariable] = indexPath
        };
    }

    private static void TryDeleteTemporaryIndex(string indexPath)
    {
        try
        {
            if (File.Exists(indexPath))
            {
                File.Delete(indexPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
