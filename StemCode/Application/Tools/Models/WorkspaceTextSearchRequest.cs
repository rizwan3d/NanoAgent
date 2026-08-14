namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceTextSearchRequest(
    string Query,
    string? Path,
    bool CaseSensitive);
