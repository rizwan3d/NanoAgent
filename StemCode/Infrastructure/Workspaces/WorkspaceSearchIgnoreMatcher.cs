using StemCode.Application.Utilities;

namespace StemCode.Infrastructure.Workspaces;

/// <summary>
/// Ignore rules used by the file search tool. The project's existing
/// <c>.gitignore</c> is respected first (it is the primary, project-level
/// ignore source), followed by the StemCode-specific
/// <c>.stemcode/.stemcodeignore</c> rules.
///
/// The two sources are evaluated independently. The <c>includeIgnored</c>
/// flag (driven by the search tool's <c>includeIgnored</c> option) overrides
/// only the <c>.gitignore</c> exclusions; <c>.stemcodeignore</c> always
/// applies and cannot be bypassed through that flag.
/// </summary>
internal sealed class WorkspaceSearchIgnoreMatcher
{
    private const string GitIgnoreRelativePath = ".gitignore";

    public WorkspaceSearchIgnoreMatcher(
        WorkspaceIgnoreMatcher gitIgnore,
        WorkspaceIgnoreMatcher stemCodeIgnore)
    {
        GitIgnore = gitIgnore;
        StemCodeIgnore = stemCodeIgnore;
    }

    public WorkspaceIgnoreMatcher GitIgnore { get; }

    public WorkspaceIgnoreMatcher StemCodeIgnore { get; }

    public static WorkspaceSearchIgnoreMatcher Load(string workspaceRoot)
    {
        return new WorkspaceSearchIgnoreMatcher(
            WorkspaceIgnoreMatcher.Load(workspaceRoot, [GitIgnoreRelativePath]),
            WorkspaceIgnoreMatcher.Load(workspaceRoot, [WorkspaceIgnoreMatcher.StemCodeIgnoreRelativePath]));
    }

    /// <summary>
    /// Returns <c>true</c> when the path is excluded. When
    /// <paramref name="includeIgnored"/> is <c>true</c>, only
    /// <c>.stemcodeignore</c> exclusions apply (the <c>.gitignore</c>
    /// exclusions are overridden); otherwise both sources are enforced.
    /// </summary>
    public bool IsIgnored(
        string fullPath,
        bool isDirectory,
        bool includeIgnored)
    {
        if (includeIgnored)
        {
            return StemCodeIgnore.IsIgnored(fullPath, isDirectory);
        }

        return GitIgnore.IsIgnored(fullPath, isDirectory) ||
               StemCodeIgnore.IsIgnored(fullPath, isDirectory);
    }

    public bool TryGetIgnoreSource(
        string fullPath,
        bool isDirectory,
        out string sourceDisplayPath)
    {
        if (GitIgnore.TryGetIgnoreSource(fullPath, isDirectory, out string gitSource))
        {
            sourceDisplayPath = gitSource;
            return true;
        }

        return StemCodeIgnore.TryGetIgnoreSource(fullPath, isDirectory, out sourceDisplayPath);
    }
}
