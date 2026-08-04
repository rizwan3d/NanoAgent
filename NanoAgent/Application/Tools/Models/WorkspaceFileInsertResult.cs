namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileInsertResult(
    string Path,
    int Line,
    int InsertedLineCount,
    int CharacterCount,
    int AddedLineCount,
    int RemovedLineCount,
    WorkspaceFileWritePreviewLine[] PreviewLines,
    int RemainingPreviewLineCount);
