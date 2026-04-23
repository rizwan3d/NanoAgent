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

    public string Description => "Apply a focused multi-file patch within the current workspace. Patch text must start with *** Begin Patch and end with *** End Patch.";

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
              "description": "Patch text using the apply_patch format. The first non-empty line must be exactly *** Begin Patch and the final non-empty line must be exactly *** End Patch."
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
            string repairGuidance = BuildPatchRepairGuidance(exception.Message);
            return ToolResultFactory.InvalidArguments(
                "invalid_patch",
                repairGuidance,
                new ToolRenderPayload(
                    "Patch rejected",
                    repairGuidance));
        }
        if (executionResult.EditTransaction is not null)
        {
            context.Session.RecordFileEditTransaction(executionResult.EditTransaction);
        }
        WorkspaceApplyPatchResult result = executionResult.Result;
        SessionStateToolRecorder.RecordApplyPatch(context.Session, result);

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

    private static string BuildPatchRepairGuidance(string parserMessage)
    {
        string normalizedMessage = string.IsNullOrWhiteSpace(parserMessage)
            ? "Patch text is not valid apply_patch format."
            : parserMessage.Trim();

        return
            $"{normalizedMessage} " +
            "Call apply_patch again with corrected patch text. " +
            "The patch argument must include the complete intended patch, its first non-empty line must be exactly '*** Begin Patch', and its final non-empty line must be exactly '*** End Patch'.";
    }

}
