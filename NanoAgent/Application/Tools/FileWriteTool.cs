using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class FileWriteTool : ITool
{
    private readonly IWorkspaceFileService _workspaceFileService;

    public FileWriteTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService;
    }

    public string Description => "Write UTF-8 text content to a file in the current workspace.";

    public string Name => AgentToolNames.FileWrite;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["edit"],
          "filePaths": [
            {
              "argumentName": "path",
              "kind": "Write",
              "allowedRoots": ["."]
            }
          ]
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Path to the file, relative to the workspace root."
            },
            "content": {
              "type": "string",
              "description": "Full UTF-8 text content to write."
            },
            "overwrite": {
              "type": "boolean",
              "description": "Whether to overwrite an existing file. Defaults to true."
            }
          },
          "required": ["path", "content"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "path", out string? path))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_path",
                "Tool 'file_write' requires a non-empty 'path' string.",
                new ToolRenderPayload(
                    "Invalid file_write arguments",
                    "Provide a non-empty 'path' string."));
        }

        if (!ToolArguments.TryGetString(context.Arguments, "content", out string? content, trim: false))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_content",
                "Tool 'file_write' requires a 'content' string.",
                new ToolRenderPayload(
                    "Invalid file_write arguments",
                    "Provide a 'content' string to write."));
        }

        string safePath = path!;
        string safeContent = content!;

        bool overwrite = ToolArguments.GetBoolean(context.Arguments, "overwrite", defaultValue: true);

        WorkspaceFileWriteExecutionResult executionResult = await _workspaceFileService.WriteFileWithTrackingAsync(
            safePath,
            safeContent,
            overwrite,
            cancellationToken);
        context.Session.RecordFileEditTransaction(executionResult.EditTransaction);
        WorkspaceFileWriteResult result = executionResult.Result;

        string renderText = result.OverwroteExistingFile
            ? $"Updated {result.Path} (+{result.AddedLineCount} -{result.RemovedLineCount})."
            : $"Created {result.Path} (+{result.AddedLineCount} -{result.RemovedLineCount}).";

        return ToolResultFactory.Success(
            $"Wrote file '{result.Path}'.",
            result,
            ToolJsonContext.Default.WorkspaceFileWriteResult,
            new ToolRenderPayload(
                $"File written: {result.Path}",
                renderText));
    }

}
