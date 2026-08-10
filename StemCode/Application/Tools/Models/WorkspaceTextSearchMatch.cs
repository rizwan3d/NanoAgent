namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceTextSearchMatch(
    string Path,
    int LineNumber,
    string LineText);
