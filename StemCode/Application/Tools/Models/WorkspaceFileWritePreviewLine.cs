namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceFileWritePreviewLine(
    int LineNumber,
    string Kind,
    string Text);
