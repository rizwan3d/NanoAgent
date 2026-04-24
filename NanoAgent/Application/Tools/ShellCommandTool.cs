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

    public string Description => "Run an OS-native shell command in the current workspace to inspect files, probe toolchains, scaffold projects, install or restore dependencies, build, test, lint, or execute short multi-command chains, and capture stdout, stderr, and exit code.";

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
              "description": "Optional working directory relative to the workspace root."
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

        ShellCommandExecutionResult result = await _shellCommandService.ExecuteAsync(
            new ShellCommandExecutionRequest(
                safeCommand,
                ToolArguments.GetOptionalString(context.Arguments, "workingDirectory"),
                sandboxPermissions,
                justification,
                prefixRule),
            cancellationToken);
        SessionStateToolRecorder.RecordShellCommand(context.Session, result);

        string renderText =
            $"Working directory: {result.WorkingDirectory}{Environment.NewLine}" +
            $"Sandbox permissions: {result.SandboxPermissions}{Environment.NewLine}" +
            $"Exit code: {result.ExitCode}{Environment.NewLine}" +
            $"STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{result.StandardError}";

        return ToolResultFactory.Success(
            $"Executed shell command '{result.Command}' with exit code {result.ExitCode}.",
            result,
            ToolJsonContext.Default.ShellCommandExecutionResult,
            new ToolRenderPayload(
                $"Shell command: {result.Command}",
                renderText));
    }

}
