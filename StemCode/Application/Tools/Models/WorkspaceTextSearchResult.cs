namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceTextSearchResult(
    string Query,
    string Path,
    IReadOnlyList<WorkspaceTextSearchMatch> Matches);
