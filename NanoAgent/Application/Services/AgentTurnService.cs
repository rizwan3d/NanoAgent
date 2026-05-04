using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Services;

internal sealed class AgentTurnService : IAgentTurnService
{
    private const string DirectShellPrefix = "!";

    private readonly IConversationPipeline _conversationPipeline;
    private readonly IAgentProfileResolver _profileResolver;
    private readonly IShellCommandService? _shellCommandService;

    public AgentTurnService(
        IConversationPipeline conversationPipeline,
        IAgentProfileResolver profileResolver,
        IShellCommandService? shellCommandService = null)
    {
        _conversationPipeline = conversationPipeline;
        _profileResolver = profileResolver;
        _shellCommandService = shellCommandService;
    }

    public async Task<ConversationTurnResult> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TryParseDirectShellCommand(request.UserInput, out string? command))
        {
            return await RunDirectShellCommandAsync(
                command,
                request,
                cancellationToken);
        }

        if (!TryParseLeadingAgentMention(
                request.UserInput,
                out string? agentName,
                out string? delegatedInput))
        {
            return await ProcessWithOptionalAttachmentsAsync(
                request.UserInput,
                request,
                cancellationToken);
        }

        IAgentProfile mentionedProfile;
        try
        {
            mentionedProfile = _profileResolver.Resolve(agentName);
        }
        catch (ArgumentException)
        {
            return ConversationTurnResult.AssistantMessage(
                $"Unknown agent '@{agentName}'. Available subagents: {FormatProfileNames(_profileResolver.List().Where(static profile => profile.Mode == AgentProfileMode.Subagent))}.");
        }

        if (mentionedProfile.Mode != AgentProfileMode.Subagent)
        {
            return ConversationTurnResult.AssistantMessage(
                $"Agent '@{mentionedProfile.Name}' is a primary profile. Use /profile {mentionedProfile.Name} to switch primary profiles.");
        }

        if (string.IsNullOrWhiteSpace(delegatedInput))
        {
            return ConversationTurnResult.AssistantMessage(
                $"Tell '@{mentionedProfile.Name}' what to do, for example: @{mentionedProfile.Name} inspect the authentication flow.");
        }

        IAgentProfile originalProfile = request.Session.AgentProfile;
        request.Session.SetAgentProfile(mentionedProfile);

        try
        {
            return await ProcessWithOptionalAttachmentsAsync(
                delegatedInput,
                request,
                cancellationToken);
        }
        finally
        {
            request.Session.SetAgentProfile(originalProfile);
        }
    }

    private Task<ConversationTurnResult> ProcessWithOptionalAttachmentsAsync(
        string input,
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        return request.Attachments.Count == 0
            ? _conversationPipeline.ProcessAsync(
                input,
                request.Session,
                request.ProgressSink,
                cancellationToken)
            : _conversationPipeline.ProcessAsync(
                input,
                request.Session,
                request.ProgressSink,
                request.Attachments,
                cancellationToken);
    }

    private async Task<ConversationTurnResult> RunDirectShellCommandAsync(
        string command,
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return ConversationTurnResult.AssistantMessage(
                "Enter a shell command after !.");
        }

        if (_shellCommandService is null)
        {
            return ConversationTurnResult.AssistantMessage(
                "Direct shell commands are unavailable in this session.");
        }

        string effectiveWorkingDirectory;
        try
        {
            effectiveWorkingDirectory = request.Session.ResolvePathFromWorkingDirectory(null);
        }
        catch (InvalidOperationException exception)
        {
            return ConversationTurnResult.AssistantMessage(exception.Message);
        }

        ShellCommandExecutionResult result = await _shellCommandService.ExecuteAsync(
            new ShellCommandExecutionRequest(
                command,
                effectiveWorkingDirectory,
                ShellCommandSandboxPermissions.RequireEscalated,
                Justification: "User-entered direct shell command."),
            cancellationToken);
        SessionStateToolRecorder.RecordShellCommand(request.Session, result);

        string? sessionDirectoryUpdate = UpdateSessionWorkingDirectoryAfterCd(
            request.Session,
            command,
            effectiveWorkingDirectory,
            result.ExitCode);
        ToolExecutionBatchResult batchResult = CreateDirectShellBatchResult(
            result,
            request.Session.WorkingDirectory,
            sessionDirectoryUpdate);
        return ConversationTurnResult.ToolExecution(batchResult);
    }

    private static ToolExecutionBatchResult CreateDirectShellBatchResult(
        ShellCommandExecutionResult result,
        string sessionWorkingDirectory,
        string? sessionDirectoryUpdate)
    {
        string sessionDirectoryLine = string.IsNullOrWhiteSpace(sessionDirectoryUpdate)
            ? string.Empty
            : sessionDirectoryUpdate + Environment.NewLine;
        string renderText =
            $"Working directory: {result.WorkingDirectory}{Environment.NewLine}" +
            $"Session working directory: {sessionWorkingDirectory}{Environment.NewLine}" +
            sessionDirectoryLine +
            $"Exit code: {result.ExitCode}{Environment.NewLine}" +
            $"STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{result.StandardError}";
        string message = $"Ran shell command '{result.Command}' with exit code {result.ExitCode}.";
        if (!string.IsNullOrWhiteSpace(sessionDirectoryUpdate))
        {
            message += " " + sessionDirectoryUpdate;
        }

        ToolResult toolResult = ToolResultFactory.Success(
            message,
            result,
            ToolJsonContext.Default.ShellCommandExecutionResult,
            new ToolRenderPayload(
                $"Shell command: {result.Command}",
                renderText));

        return new ToolExecutionBatchResult(
            [
                new ToolInvocationResult(
                    "direct-shell-" + Guid.NewGuid().ToString("N"),
                    AgentToolNames.ShellCommand,
                    toolResult)
            ]);
    }

    private static string? UpdateSessionWorkingDirectoryAfterCd(
        ReplSessionContext session,
        string command,
        string commandWorkingDirectory,
        int exitCode)
    {
        if (exitCode != 0 ||
            !TryGetCdTarget(command, out string? targetPath))
        {
            return null;
        }

        return session.TrySetWorkingDirectory(targetPath!, commandWorkingDirectory, out string? error)
            ? $"Session working directory is now '{session.WorkingDirectory}'."
            : $"Session working directory stayed '{session.WorkingDirectory}': {error}";
    }

    private static bool TryGetCdTarget(
        string command,
        out string? targetPath)
    {
        targetPath = null;

        IReadOnlyList<ShellCommandSegment> segments = ShellCommandText.ParseSegments(command);
        if (segments.Count != 1 ||
            segments[0].Condition != ShellCommandSegmentCondition.Always)
        {
            return false;
        }

        string[] tokens = ShellCommandText.Tokenize(segments[0].CommandText);
        if (tokens.Length < 2)
        {
            return false;
        }

        string commandName = ShellCommandText.NormalizeCommandToken(tokens[0]);
        if (!string.Equals(commandName, "cd", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (tokens.Length == 2)
        {
            targetPath = tokens[1];
            return true;
        }

        if (tokens.Length == 3 &&
            string.Equals(tokens[1], "/d", StringComparison.OrdinalIgnoreCase))
        {
            targetPath = tokens[2];
            return true;
        }

        return false;
    }

    private static bool TryParseDirectShellCommand(
        string input,
        out string command)
    {
        string trimmedInput = input.Trim();
        if (!trimmedInput.StartsWith(DirectShellPrefix, StringComparison.Ordinal))
        {
            command = string.Empty;
            return false;
        }

        command = trimmedInput[DirectShellPrefix.Length..].Trim();
        return true;
    }

    private static bool TryParseLeadingAgentMention(
        string input,
        out string agentName,
        out string delegatedInput)
    {
        agentName = string.Empty;
        delegatedInput = string.Empty;

        string trimmedInput = input.Trim();
        if (!trimmedInput.StartsWith('@'))
        {
            return false;
        }

        int index = 1;
        while (index < trimmedInput.Length && IsAgentNameCharacter(trimmedInput[index]))
        {
            index++;
        }

        if (index == 1)
        {
            return false;
        }

        agentName = trimmedInput[1..index];
        delegatedInput = trimmedInput[index..].Trim();
        return true;
    }

    private static bool IsAgentNameCharacter(char value)
    {
        return char.IsLetterOrDigit(value) ||
               value is '-' or '_';
    }

    private static string FormatProfileNames(IEnumerable<IAgentProfile> profiles)
    {
        return string.Join(
            ", ",
            profiles.Select(static profile => profile.Name));
    }
}
