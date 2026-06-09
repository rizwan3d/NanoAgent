namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileSearchRequest(
    string Query,
    string? Path,
    bool CaseSensitive,
    string? Glob = null,
    bool Fuzzy = false,
    int Limit = 200);
