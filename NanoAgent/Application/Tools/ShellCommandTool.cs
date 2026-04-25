using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class ShellCommandTool : ITool
{
    private readonly IShellCommandService _shellCommandService;

    public ShellCommandTool(IShellCommandService shellCommandService)
    {
        _shellCommandService = shellCommandService;
    }

    public string Description => "Run an OS-native shell command in the current workspace to inspect files, probe toolchains, scaffold projects, install or restore dependencies, build, test, lint, or execute short multi-command chains, optionally attached to a pseudo-terminal, and capture stdout, stderr, and exit code.";

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
            "prefixRuleArgumentName": "prefix_rule",
            "allowedCommands": [
              "bun",
              "cargo",
              "cat",
              "cd",
              "clang",
              "clang++",
              "cmake",
              "composer",
              "csc",
              "deno",
              "dir",
              "dotnet",
              "find",
              "findstr",
              "gcc",
              "g++",
              "Get-ChildItem",
              "Get-Command",
              "Get-Content",
              "Get-Item",
              "Get-Location",
              "git",
              "go",
              "gradle",
              "grep",
              "head",
              "java",
              "javac",
              "kotlinc",
              "ls",
              "make",
              "mkdir",
              "msbuild",
              "mvn",
              "node",
              "npm",
              "npx",
              "nuget",
              "php",
              "pip",
              "pip3",
              "pnpm",
              "poetry",
              "pwd",
              "py",
              "pytest",
              "python",
              "python3",
              "rg",
              "ruff",
              "sed",
              "Select-String",
              "swift",
              "tsc",
              "type",
              "uv",
              "uvx",
              "where",
              "which",
              "yarn"
            ]
          }
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "command": {
              "type": "string",
              "description": "Shell command to execute."
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
            }
          },
          "required": ["command"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

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

        ShellCommandExecutionResult result = await _shellCommandService.ExecuteAsync(
            new ShellCommandExecutionRequest(
                safeCommand,
                effectiveWorkingDirectory,
                sandboxPermissions,
                justification,
                prefixRule,
                pseudoTerminal),
            cancellationToken);
        SessionStateToolRecorder.RecordShellCommand(context.Session, result);

        string? sessionDirectoryUpdate = UpdateSessionWorkingDirectoryAfterCd(
            context.Session,
            safeCommand,
            effectiveWorkingDirectory,
            result.ExitCode);
        string renderText =
            $"Working directory: {result.WorkingDirectory}{Environment.NewLine}" +
            $"Session working directory: {context.Session.WorkingDirectory}{Environment.NewLine}" +
            $"Sandbox mode: {result.SandboxMode}{Environment.NewLine}" +
            $"Sandbox permissions: {result.SandboxPermissions}{Environment.NewLine}" +
            $"Sandbox enforcement: {result.SandboxEnforcement}{Environment.NewLine}" +
            $"Pseudo terminal: {result.PseudoTerminal}{Environment.NewLine}" +
            $"Exit code: {result.ExitCode}{Environment.NewLine}" +
            $"STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{result.StandardError}";
        string message = $"Executed shell command '{result.Command}' with exit code {result.ExitCode}.";
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

}
