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
        SessionStateToolRecorder.RecordShellCommand(context.Session, result);

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
