using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class ShellCommandTool : ITool
{
    private const string UnsupportedSandboxEnforcement = "unsupported";
    private const string UnsupportedSandboxNote = "OS-level shell sandboxing is not available on this platform; the command ran after NanoAgent permission approval without OS-level sandbox enforcement.";

    private readonly IShellCommandService _shellCommandService;

    public ShellCommandTool(IShellCommandService shellCommandService)
    {
        _shellCommandService = shellCommandService;
    }

    public string Description => "Run an OS-native shell command in the current workspace to inspect files, probe toolchains, scaffold projects, install or restore dependencies, build, test, lint, or execute short multi-command chains; commands can run in the foreground optionally attached to a pseudo-terminal or run interactive commands or as background terminals that can be read or stopped later.";

    public string Name => AgentToolNames.ShellCommand;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["bash"],
          "filePaths": [
            {
              "argumentName": "workingDirectory",
              "kind": "List",
              "allowedRoots": ["."]
            }
          ],
          "shell": {
            "commandArgumentName": "command",
            "sandboxPermissionsArgumentName": "sandbox_permissions",
            "justificationArgumentName": "justification",
            "prefixRuleArgumentName": "prefix_rule"
          }
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "command": {
              "type": "string",
              "description": "Shell command to execute. Required for terminal_action 'run' and 'start'."
            },
            "terminal_action": {
              "type": "string",
              "enum": ["run", "start", "read", "stop"],
              "description": "Use 'run' for foreground execution, 'start' to create a background terminal, 'read' to collect new output from a background terminal, or 'stop' to terminate one. Defaults to 'run'."
            },
            "terminal_id": {
              "type": "string",
              "description": "Background terminal id. Required for terminal_action 'read' and 'stop'."
            },
            "workingDirectory": {
              "type": "string",
              "description": "Optional working directory relative to the current session working directory. Defaults to the current session working directory."
            },
            "sandbox_permissions": {
              "type": "string",
              "enum": ["use_default", "require_escalated"],
              "description": "Use 'use_default' for normal sandboxed execution. Use 'require_escalated' only when the command truly needs to run outside the configured sandbox."
            },
            "justification": {
              "type": "string",
              "description": "Required when sandbox_permissions is 'require_escalated'; briefly explain why sandbox escalation is needed."
            },
            "prefix_rule": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Optional command prefix that may be reused for similar future approvals."
            },
            "pty": {
              "type": "boolean",
              "description": "When true, run the command attached to a pseudo-terminal so terminal-aware programs can emit interactive-style output."
            },
            "background": {
              "type": "boolean",
              "description": "Shortcut for terminal_action 'start'."
            }
          },
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetTerminalAction(context, out string terminalAction, out ToolResult? actionError))
        {
            return actionError!;
        }

        if (string.Equals(terminalAction, "read", StringComparison.Ordinal))
        {
            return await ReadBackgroundTerminalAsync(context, cancellationToken);
        }

        if (string.Equals(terminalAction, "stop", StringComparison.Ordinal))
        {
            return await StopBackgroundTerminalAsync(context, cancellationToken);
        }

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "command", out string? command))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_command",
                "Tool 'shell_command' requires a non-empty 'command' string.",
                new ToolRenderPayload(
                    "Invalid shell_command arguments",
                    "Provide a non-empty 'command' string."));
        }

        string safeCommand = command!;
        if (!ShellCommandSandboxArguments.TryGetSandboxPermissions(
                context.Arguments,
                "sandbox_permissions",
                out ShellCommandSandboxPermissions sandboxPermissions,
                out string? invalidSandboxPermissions))
        {
            return ToolResultFactory.InvalidArguments(
                "invalid_sandbox_permissions",
                $"Tool 'shell_command' received invalid sandbox_permissions value '{invalidSandboxPermissions}'.",
                new ToolRenderPayload(
                    "Invalid shell_command arguments",
                    "sandbox_permissions must be 'use_default' or 'require_escalated'."));
        }

        string? justification = ToolArguments.GetOptionalString(context.Arguments, "justification");
        if (sandboxPermissions == ShellCommandSandboxPermissions.RequireEscalated &&
            string.IsNullOrWhiteSpace(justification))
        {
            return ToolResultFactory.InvalidArguments(
                "sandbox_justification_required",
                "Tool 'shell_command' requires a non-empty 'justification' when sandbox_permissions is 'require_escalated'.",
                new ToolRenderPayload(
                    "Invalid shell_command arguments",
                    "Provide a justification for sandbox escalation."));
        }

        IReadOnlyList<string> prefixRule = ToolArguments.GetOptionalStringArray(context.Arguments, "prefix_rule");
        bool pseudoTerminal = ToolArguments.GetBoolean(context.Arguments, "pty");
        string? requestedWorkingDirectory = ToolArguments.GetOptionalString(context.Arguments, "workingDirectory");
        string effectiveWorkingDirectory;
        try
        {
            effectiveWorkingDirectory = context.Session.ResolvePathFromWorkingDirectory(requestedWorkingDirectory);
        }
        catch (InvalidOperationException exception)
        {
            return ToolResultFactory.InvalidArguments(
                "path_outside_workspace",
                exception.Message,
                new ToolRenderPayload(
                    "Invalid shell_command arguments",
                    exception.Message));
        }

        ShellCommandExecutionRequest executionRequest = CreateExecutionRequest(
            safeCommand,
            effectiveWorkingDirectory,
            sandboxPermissions,
            justification,
            prefixRule,
            pseudoTerminal,
            terminalAction);
        ShellCommandExecutionResult result = string.Equals(terminalAction, "start", StringComparison.Ordinal)
            ? await _shellCommandService.StartBackgroundAsync(executionRequest, cancellationToken)
            : await _shellCommandService.ExecuteAsync(executionRequest, cancellationToken);
        SessionStateToolRecorder.RecordShellCommand(context.Session, result);

        if (string.Equals(terminalAction, "start", StringComparison.Ordinal))
        {
            return CreateShellToolResult(
                context,
                result,
                sessionDirectoryUpdate: null);
        }

        string? sessionDirectoryUpdate = UpdateSessionWorkingDirectoryAfterCd(
            context.Session,
            safeCommand,
            effectiveWorkingDirectory,
            result.ExitCode);
        return CreateShellToolResult(
            context,
            result,
            sessionDirectoryUpdate);
    }

    private async Task<ToolResult> ReadBackgroundTerminalAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "terminal_id", out string? terminalId))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_terminal_id",
                "Tool 'shell_command' requires a non-empty 'terminal_id' when terminal_action is 'read'.",
                new ToolRenderPayload(
                    "Invalid shell_command arguments",
                    "Provide a background terminal id."));
        }

        ShellCommandExecutionResult result = await _shellCommandService.ReadBackgroundAsync(
            terminalId!,
            cancellationToken);
        return CreateBackgroundTerminalToolResult(context, result);
    }

    private async Task<ToolResult> StopBackgroundTerminalAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "terminal_id", out string? terminalId))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_terminal_id",
                "Tool 'shell_command' requires a non-empty 'terminal_id' when terminal_action is 'stop'.",
                new ToolRenderPayload(
                    "Invalid shell_command arguments",
                    "Provide a background terminal id."));
        }

        ShellCommandExecutionResult result = await _shellCommandService.StopBackgroundAsync(
            terminalId!,
            cancellationToken);
        return CreateBackgroundTerminalToolResult(context, result);
    }

    private ToolResult CreateBackgroundTerminalToolResult(
        ToolExecutionContext context,
        ShellCommandExecutionResult result)
    {
        if (string.Equals(result.TerminalStatus, "not_found", StringComparison.Ordinal))
        {
            return ToolResultFactory.NotFound(
                "background_terminal_not_found",
                $"Background terminal '{result.TerminalId}' was not found.",
                new ToolRenderPayload(
                    "Background terminal not found",
                    result.StandardError));
        }

        SessionStateToolRecorder.RecordShellCommand(context.Session, result);
        return CreateShellToolResult(
            context,
            result,
            sessionDirectoryUpdate: null);
    }

    private ToolResult CreateShellToolResult(
        ToolExecutionContext context,
        ShellCommandExecutionResult result,
        string? sessionDirectoryUpdate)
    {
        string renderText =
            $"Working directory: {result.WorkingDirectory}{Environment.NewLine}" +
            $"Session working directory: {context.Session.WorkingDirectory}{Environment.NewLine}" +
            $"Sandbox mode: {result.SandboxMode}{Environment.NewLine}" +
            $"Sandbox permissions: {result.SandboxPermissions}{Environment.NewLine}" +
            $"Sandbox enforcement: {result.SandboxEnforcement}{Environment.NewLine}" +
            CreateSandboxNoteLine(result) +
            $"Pseudo terminal: {result.PseudoTerminal}{Environment.NewLine}" +
            CreateBackgroundTerminalLines(result) +
            CreateExitCodeLine(result) +
            $"STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{result.StandardError}";
        string message = CreateResultMessage(result);
        if (IsUnsupportedSandboxResult(result))
        {
            message += " " + UnsupportedSandboxNote;
        }

        if (!string.IsNullOrWhiteSpace(sessionDirectoryUpdate))
        {
            message += " " + sessionDirectoryUpdate;
        }

        return ToolResultFactory.Success(
            message,
            result,
            ToolJsonContext.Default.ShellCommandExecutionResult,
            new ToolRenderPayload(
                $"Shell command: {result.Command}",
                renderText));
    }

    private static ShellCommandExecutionRequest CreateExecutionRequest(
        string command,
        string effectiveWorkingDirectory,
        ShellCommandSandboxPermissions sandboxPermissions,
        string? justification,
        IReadOnlyList<string> prefixRule,
        bool pseudoTerminal,
        string terminalAction)
    {
        return new ShellCommandExecutionRequest(
            command,
            effectiveWorkingDirectory,
            sandboxPermissions,
            justification,
            prefixRule,
            string.Equals(terminalAction, "start", StringComparison.Ordinal)
                ? false
                : pseudoTerminal);
    }

    private static bool TryGetTerminalAction(
        ToolExecutionContext context,
        out string terminalAction,
        out ToolResult? error)
    {
        string? requestedAction = ToolArguments.GetOptionalString(
            context.Arguments,
            "terminal_action");
        bool background = ToolArguments.GetBoolean(context.Arguments, "background");
        terminalAction = string.IsNullOrWhiteSpace(requestedAction)
            ? background ? "start" : "run"
            : requestedAction.Trim().ToLowerInvariant();
        error = null;

        if (terminalAction is "run" or "start" or "read" or "stop")
        {
            return true;
        }

        error = ToolResultFactory.InvalidArguments(
            "invalid_terminal_action",
            $"Tool 'shell_command' received invalid terminal_action '{requestedAction}'. Expected 'run', 'start', 'read', or 'stop'.",
            new ToolRenderPayload(
                "Invalid shell_command arguments",
                "terminal_action must be 'run', 'start', 'read', or 'stop'."));
        return false;
    }

    private static string CreateBackgroundTerminalLines(ShellCommandExecutionResult result)
    {
        if (!result.Background)
        {
            return string.Empty;
        }

        return
            $"Background terminal: {result.TerminalId}{Environment.NewLine}" +
            $"Terminal action: {result.TerminalAction}{Environment.NewLine}" +
            $"Terminal status: {result.TerminalStatus}{Environment.NewLine}";
    }

    private static string CreateExitCodeLine(ShellCommandExecutionResult result)
    {
        return result.Background &&
               string.Equals(result.TerminalStatus, "running", StringComparison.Ordinal)
            ? $"Exit code: pending{Environment.NewLine}"
            : $"Exit code: {result.ExitCode}{Environment.NewLine}";
    }

    private static string CreateResultMessage(ShellCommandExecutionResult result)
    {
        if (!result.Background)
        {
            return $"Executed shell command '{result.Command}' with exit code {result.ExitCode}.";
        }

        return result.TerminalAction switch
        {
            "start" => $"Started background terminal '{result.TerminalId}' for command '{result.Command}'.",
            "read" => $"Read background terminal '{result.TerminalId}' with status '{result.TerminalStatus}'.",
            "stop" => $"Stopped background terminal '{result.TerminalId}'.",
            _ => $"Updated background terminal '{result.TerminalId}' with status '{result.TerminalStatus}'."
        };
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

    private static string CreateSandboxNoteLine(ShellCommandExecutionResult result)
    {
        return IsUnsupportedSandboxResult(result)
            ? $"Sandbox note: {UnsupportedSandboxNote}{Environment.NewLine}"
            : string.Empty;
    }

    private static bool IsUnsupportedSandboxResult(ShellCommandExecutionResult result)
    {
        return string.Equals(
            result.SandboxEnforcement,
            UnsupportedSandboxEnforcement,
            StringComparison.Ordinal);
    }

}
