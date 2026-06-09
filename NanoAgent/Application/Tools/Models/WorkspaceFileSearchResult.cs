namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileSearchResult(
    string Query,
    string Path,
    IReadOnlyList<string> Matches,
    string? Glob = null,
    bool Fuzzy = false,
    int Limit = 200,
    bool CaseSensitive = false);
