namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileDeleteResult(
    string Path,
    int DeletedCharacterCount,
    int AddedLineCount,
    int RemovedLineCount,
    WorkspaceFileWritePreviewLine[] PreviewLines,
    int RemainingPreviewLineCount);
