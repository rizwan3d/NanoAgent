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

    public string Description => "Run an OS-native shell command in the current workspace and capture stdout, stderr, and exit code.";

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
              "allowedCommands": [
              "cat",
              "dir",
              "dotnet",
              "find",
              "findstr",
              "Get-ChildItem",
              "Get-Content",
              "Get-Item",
              "Get-Location",
              "git",
              "grep",
              "head",
              "ls",
              "pwd",
              "rg",
              "sed",
              "Select-String",
              "type",
              "which"
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

        ShellCommandExecutionResult result = await _shellCommandService.ExecuteAsync(
            new ShellCommandExecutionRequest(
                safeCommand,
                ToolArguments.GetOptionalString(context.Arguments, "workingDirectory")),
            cancellationToken);

        string renderText =
            $"Working directory: {result.WorkingDirectory}{Environment.NewLine}" +
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
