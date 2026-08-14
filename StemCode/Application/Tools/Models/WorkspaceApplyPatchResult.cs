namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceApplyPatchResult(
    int FileCount,
    int AddedLineCount,
    int RemovedLineCount,
    IReadOnlyList<WorkspaceApplyPatchFileResult> Files);
