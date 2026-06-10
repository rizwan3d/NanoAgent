namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileSearchResult(
    string Query,
    string Path,
    IReadOnlyList<WorkspaceFileSearchMatch> Matches,
    string? Glob = null,
    bool Fuzzy = false,
    int Limit = 200,
    bool CaseSensitive = false,
    string Mode = WorkspaceFileSearchModes.Substring,
    bool Regex = false,
    bool WholeWord = false,
    int Offset = 0,
    string? Cursor = null,
    string? NextCursor = null,
    bool HasMore = false,
    int TotalMatchCount = 0,
    bool IncludeHidden = false,
    bool IncludeGenerated = false,
    bool IncludeIgnored = false,
    IReadOnlyList<string>? IncludeGlobs = null,
    IReadOnlyList<string>? ExcludeGlobs = null);

public sealed record WorkspaceFileSearchMatch(
    string Path,
    int Score,
    string MatchKind);

public static class WorkspaceFileSearchModes
{
    public const string Substring = "substring";
    public const string Fuzzy = "fuzzy";
    public const string Exact = "exact";
    public const string Regex = "regex";
    public const string GlobOnly = "glob_only";

    public static bool IsSupported(string? value)
    {
        return value is Substring or Fuzzy or Exact or Regex or GlobOnly;
    }
}
