namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileWriteResult(
    string Path,
    bool OverwroteExistingFile,
    int CharacterCount,
    int AddedLineCount,
    int RemovedLineCount,
    WorkspaceFileWritePreviewLine[] PreviewLines,
    int RemainingPreviewLineCount);
