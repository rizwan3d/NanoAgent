namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceSearchAndReplaceResult(
    string Path,
    string Search,
    string Replace,
    bool UseRegex,
    bool CaseSensitive,
    int ReplacementCount,
    int CharacterCount,
    int AddedLineCount,
    int RemovedLineCount,
    WorkspaceFileWritePreviewLine[] PreviewLines,
    int RemainingPreviewLineCount);
