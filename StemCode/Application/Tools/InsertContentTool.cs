using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Application.Tools.Models;
using StemCode.Application.Tools.Serialization;

namespace StemCode.Application.Tools;

internal sealed class InsertContentTool : ITool
{
    private readonly IWorkspaceFileService _workspaceFileService;

    public InsertContentTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService;
    }

    public string Description => "Insert UTF-8 text at a specific 1-based line position in an existing file from the current session working directory in the workspace.";

    public string Name => AgentToolNames.InsertContent;

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
              "description": "Path to the file, relative to the current session working directory."
            },
            "line": {
              "type": "integer",
              "description": "1-based line number to insert before. Use totalLines + 1 to append at the end."
            },
            "content": {
              "type": "string",
              "description": "UTF-8 text to insert. May contain multiple lines."
            }
          },
          "required": ["path", "line", "content"],
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
                "Tool 'insert_content' requires a non-empty 'path' string.",
                new ToolRenderPayload(
                    "Invalid insert_content arguments",
                    "Provide a non-empty 'path' string."));
        }

        if (!ToolArguments.TryGetInt32(context.Arguments, "line", out int line) || line <= 0)
        {
            return ToolResultFactory.InvalidArguments(
                "missing_line",
                "Tool 'insert_content' requires a positive integer 'line'.",
                new ToolRenderPayload(
                    "Invalid insert_content arguments",
                    "Provide a positive 1-based 'line' number."));
        }

        if (!ToolArguments.TryGetString(context.Arguments, "content", out string? content, trim: false))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_content",
                "Tool 'insert_content' requires a 'content' string.",
                new ToolRenderPayload(
                    "Invalid insert_content arguments",
                    "Provide a 'content' string to insert."));
        }

        string safePath = context.Session.ResolvePathFromWorkingDirectory(path!);
        WorkspaceFileInsertExecutionResult executionResult = await _workspaceFileService.InsertContentWithTrackingAsync(
            safePath,
            line,
            content!,
            cancellationToken);
        context.Session.RecordFileEditTransaction(executionResult.EditTransaction);

        WorkspaceFileInsertResult result = executionResult.Result;
        SessionStateToolRecorder.RecordFileInsert(context.Session, result);

        return ToolResultFactory.Success(
            $"Inserted content into '{result.Path}' at line {result.Line}.",
            result,
            ToolJsonContext.Default.WorkspaceFileInsertResult,
            new ToolRenderPayload(
                $"Content inserted: {result.Path}",
                $"Inserted {result.InsertedLineCount} {(result.InsertedLineCount == 1 ? "line" : "lines")} into {result.Path} at line {result.Line} (+{result.AddedLineCount} -{result.RemovedLineCount})."));
    }
}
