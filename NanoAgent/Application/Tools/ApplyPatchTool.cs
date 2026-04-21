using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class ApplyPatchTool : ITool
{
    private readonly IWorkspaceFileService _workspaceFileService;

    public ApplyPatchTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService;
    }

    public string Description => "Apply a focused multi-file patch within the current workspace.";

    public string Name => AgentToolNames.ApplyPatch;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["edit"],
          "patch": {
            "patchArgumentName": "patch",
            "kind": "Write",
            "allowedRoots": ["."]
          }
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "patch": {
              "type": "string",
              "description": "Patch text using the apply_patch format with *** Begin Patch / *** End Patch markers."
            }
          },
          "required": ["patch"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "patch", out string? patch, trim: false))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_patch",
                "Tool 'apply_patch' requires a non-empty 'patch' string.",
                new ToolRenderPayload(
                    "Invalid apply_patch arguments",
                    "Provide a non-empty 'patch' string."));
        }

        WorkspaceApplyPatchExecutionResult executionResult;
        try
        {
            executionResult = await _workspaceFileService.ApplyPatchWithTrackingAsync(
                patch!,
                cancellationToken);
        }
        catch (FormatException exception)
        {
            return ToolResultFactory.InvalidArguments(
                "invalid_patch",
                exception.Message,
                new ToolRenderPayload(
                    "Patch rejected",
                    exception.Message));
        }
        if (executionResult.EditTransaction is not null)
        {
            context.Session.RecordFileEditTransaction(executionResult.EditTransaction);
        }
        WorkspaceApplyPatchResult result = executionResult.Result;

        string renderText = result.Files.Count == 0
            ? "No files changed."
            : string.Join(
                Environment.NewLine,
                result.Files.Select(static file =>
                    file.PreviousPath is null
                        ? $"{file.Operation}: {file.Path} (+{file.AddedLineCount} -{file.RemovedLineCount})"
                        : $"{file.Operation}: {file.PreviousPath} -> {file.Path} (+{file.AddedLineCount} -{file.RemovedLineCount})"));

        return ToolResultFactory.Success(
            $"Applied patch to {result.FileCount} {(result.FileCount == 1 ? "file" : "files")}.",
            result,
            ToolJsonContext.Default.WorkspaceApplyPatchResult,
            new ToolRenderPayload(
                $"Applied patch ({result.FileCount} {(result.FileCount == 1 ? "file" : "files")})",
                renderText));
    }

}
