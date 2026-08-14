namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceApplyPatchFileResult(
    string Path,
    string Operation,
    string? PreviousPath,
    int AddedLineCount,
    int RemovedLineCount,
    WorkspaceFileWritePreviewLine[] PreviewLines,
    int RemainingPreviewLineCount);
