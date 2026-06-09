namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileSearchRequest(
    string Query,
    string? Path,
    bool CaseSensitive,
    string? Glob = null,
    bool Fuzzy = false,
    int Limit = 200,
    string Mode = WorkspaceFileSearchModes.Substring,
    bool Regex = false,
    bool WholeWord = false,
    int Offset = 0,
    string? Cursor = null,
    bool IncludeHidden = false,
    bool IncludeGenerated = false,
    bool IncludeIgnored = false,
    IReadOnlyList<string>? IncludeGlobs = null,
    IReadOnlyList<string>? ExcludeGlobs = null);
