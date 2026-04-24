using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class FileDeleteTool : ITool
{
    private readonly IWorkspaceFileService _workspaceFileService;

    public FileDeleteTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService;
    }

    public string Description => "Delete a file from the current workspace.";

    public string Name => AgentToolNames.FileDelete;

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
              "description": "Path to the file to delete, relative to the workspace root."
            }
          },
          "required": ["path"],
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
                "Tool 'file_delete' requires a non-empty 'path' string.",
                new ToolRenderPayload(
                    "Invalid file_delete arguments",
                    "Provide a non-empty 'path' string."));
        }

        WorkspaceFileDeleteExecutionResult executionResult = await _workspaceFileService.DeleteFileWithTrackingAsync(
            path!,
            cancellationToken);
        context.Session.RecordFileEditTransaction(executionResult.EditTransaction);

        WorkspaceFileDeleteResult result = executionResult.Result;
        SessionStateToolRecorder.RecordFileDelete(context.Session, result);

        return ToolResultFactory.Success(
            $"Deleted file '{result.Path}'.",
            result,
            ToolJsonContext.Default.WorkspaceFileDeleteResult,
            new ToolRenderPayload(
                $"File deleted: {result.Path}",
                $"Deleted {result.Path} (-{result.RemovedLineCount})."));
    }
}
