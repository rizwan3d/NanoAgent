using StemCode.Application.Utilities;
using System.Collections.Concurrent;

namespace StemCode.Infrastructure.Workspaces;

/// <summary>
/// A single workspace path that the workspace read/write policy restricts.
/// </summary>
/// <param name="FullPath">Canonical absolute path of the restricted entry.</param>
/// <param name="IsDirectory">Whether the entry is a directory subtree.</param>
/// <param name="RelativePath">Workspace-relative display path.</param>
/// <param name="SourceDisplayPath">Ignore file that produced the restriction.</param>
internal sealed record WorkspaceRestrictedPath(
    string FullPath,
    bool IsDirectory,
    string RelativePath,
    string SourceDisplayPath);

/// <summary>
/// The single source of truth for workspace read restrictions.
/// </summary>
/// <remarks>
/// <para>
/// Restrictions are declared in <c>.stemcode/.stemcodeignore</c> only. The same resolved paths
/// are consumed by <c>WorkspaceFileService</c> (tool-level checks) and by the OS sandbox planners
/// (bubblewrap mounts, <c>sandbox-exec</c> profiles, and Windows ACLs) so a path can never be
/// blocked by one layer while remaining readable through another.
/// </para>
/// <para>
/// A path that the OS sandbox would otherwise restrict but that is not matched by the policy is
/// deliberately left accessible: the ignore file is the only allowed source of denials.
/// </para>
/// </remarks>
internal sealed class WorkspaceRestrictedPathPolicy
{
    /// <summary>Upper bound on filesystem entries inspected while resolving the policy.</summary>
    private const int MaxInspectedEntries = 500_000;

    /// <summary>Upper bound on resolved restricted paths handed to the OS sandbox layers.</summary>
    private const int MaxRestrictedPaths = 50_000;

    /// <summary>Upper bound on directory nesting, a backstop against pathological link graphs.</summary>
    private const int MaxDirectoryDepth = 64;

    /// <summary>How long a resolved snapshot may be reused before the workspace is rescanned.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(
        WorkspacePath.GetPathComparer());

    public static WorkspaceRestrictedPathPolicy Empty { get; } = new(
        string.Empty,
        WorkspaceIgnoreMatcher.Load(string.Empty),
        [],
        truncated: false);

    private readonly WorkspaceIgnoreMatcher _ignoreMatcher;

    private WorkspaceRestrictedPathPolicy(
        string workspaceRoot,
        WorkspaceIgnoreMatcher ignoreMatcher,
        IReadOnlyList<WorkspaceRestrictedPath> restrictedPaths,
        bool truncated)
    {
        WorkspaceRoot = workspaceRoot;
        _ignoreMatcher = ignoreMatcher;
        RestrictedPaths = restrictedPaths;
        Truncated = truncated;
    }

    /// <summary>Canonical workspace root the policy was resolved against.</summary>
    public string WorkspaceRoot { get; }

    /// <summary>Concrete existing paths that the policy restricts, outermost first.</summary>
    public IReadOnlyList<WorkspaceRestrictedPath> RestrictedPaths { get; }

    /// <summary>Whether resolution hit a traversal or result cap and may be incomplete.</summary>
    public bool Truncated { get; }

    /// <summary>Whether the workspace declares any restricted paths at all.</summary>
    public bool HasRestrictions => RestrictedPaths.Count > 0;

    /// <summary>Whether the ignore file declared any usable rules.</summary>
    public bool HasRules => _ignoreMatcher.HasRules;

    /// <summary>
    /// Resolves the restriction policy for <paramref name="workspaceRoot"/>, reusing a recent
    /// snapshot when the ignore file and scan window are unchanged.
    /// </summary>
    /// <param name="workspaceRoot">Workspace to resolve restrictions for.</param>
    /// <param name="useCache">
    /// When <see langword="false"/>, the workspace is always rescanned. OS sandbox planning must
    /// pass <see langword="false"/> because it materializes the snapshot into mount and ACL rules
    /// and cannot fall back to rule matching for files created since the last scan.
    /// </param>
    public static WorkspaceRestrictedPathPolicy Load(string workspaceRoot, bool useCache = true)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return Empty;
        }

        string fullWorkspaceRoot;
        try
        {
            fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return Empty;
        }

        if (!Directory.Exists(fullWorkspaceRoot))
        {
            return Empty;
        }

        string ignoreStamp = ReadIgnoreFileStamp(fullWorkspaceRoot);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (useCache &&
            Cache.TryGetValue(fullWorkspaceRoot, out CacheEntry? cached) &&
            string.Equals(cached.IgnoreStamp, ignoreStamp, StringComparison.Ordinal) &&
            now - cached.ResolvedAt < CacheLifetime)
        {
            return cached.Policy;
        }

        WorkspaceRestrictedPathPolicy policy = Resolve(fullWorkspaceRoot);
        Cache[fullWorkspaceRoot] = new CacheEntry(policy, ignoreStamp, now);
        return policy;
    }

    /// <summary>
    /// Resolves a policy snapshot suitable for materializing into OS sandbox rules, bypassing the
    /// cache so newly created files are covered.
    /// </summary>
    public static WorkspaceRestrictedPathPolicy LoadForSandbox(string workspaceRoot)
    {
        return Load(workspaceRoot, useCache: false);
    }

    /// <summary>Drops every cached snapshot. Intended for tests.</summary>
    internal static void ClearCache()
    {
        Cache.Clear();
    }

    /// <summary>
    /// Whether <paramref name="fullPath"/> is restricted by the policy. Descendants of a
    /// restricted directory are restricted as well.
    /// </summary>
    public bool IsRestricted(string fullPath, bool isDirectory)
    {
        return TryGetRestrictionSource(fullPath, isDirectory, out _);
    }

    /// <summary>
    /// Whether <paramref name="fullPath"/> is restricted, reporting the ignore file that declared
    /// the restriction.
    /// </summary>
    public bool TryGetRestrictionSource(
        string fullPath,
        bool isDirectory,
        out string sourceDisplayPath)
    {
        sourceDisplayPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrEmpty(WorkspaceRoot))
        {
            return false;
        }

        string candidate;
        try
        {
            candidate = Path.GetFullPath(fullPath);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return false;
        }

        foreach (WorkspaceRestrictedPath restricted in RestrictedPaths)
        {
            if (restricted.IsDirectory
                    ? WorkspacePath.IsSamePathOrDescendant(restricted.FullPath, candidate)
                    : WorkspacePath.PathEquals(restricted.FullPath, candidate))
            {
                sourceDisplayPath = restricted.SourceDisplayPath;
                return true;
            }
        }

        // Fall back to the rules so paths that do not exist yet are still evaluated.
        return _ignoreMatcher.TryGetIgnoreSource(candidate, isDirectory, out sourceDisplayPath);
    }

    /// <summary>Restricted directory subtrees, used for subtree-scoped sandbox rules.</summary>
    public IReadOnlyList<string> GetRestrictedDirectories()
    {
        return [.. RestrictedPaths
            .Where(static restricted => restricted.IsDirectory)
            .Select(static restricted => restricted.FullPath)];
    }

    /// <summary>Restricted individual files, used for path-literal sandbox rules.</summary>
    public IReadOnlyList<string> GetRestrictedFiles()
    {
        return [.. RestrictedPaths
            .Where(static restricted => !restricted.IsDirectory)
            .Select(static restricted => restricted.FullPath)];
    }

    private static WorkspaceRestrictedPathPolicy Resolve(string workspaceRoot)
    {
        WorkspaceIgnoreMatcher ignoreMatcher = WorkspaceIgnoreMatcher.Load(workspaceRoot);
        if (!ignoreMatcher.HasRules)
        {
            return new WorkspaceRestrictedPathPolicy(
                workspaceRoot,
                ignoreMatcher,
                [],
                truncated: false);
        }

        List<WorkspaceRestrictedPath> restricted = [];
        int inspectedEntries = 0;
        bool truncated = false;

        // Directories are de-duplicated by their own canonical path, not by their symlink target,
        // so a restricted path reachable through several spellings produces a rule for each
        // spelling. Sandbox mount and ACL rules are path-based, so missing a spelling would leave
        // the path readable.
        Queue<PendingDirectory> pending = new();
        HashSet<string> visitedDirectories = new(WorkspacePath.GetPathComparer());
        string canonicalRoot = CanonicalOrOriginal(workspaceRoot);
        pending.Enqueue(new PendingDirectory(canonicalRoot, Depth: 0));
        visitedDirectories.Add(canonicalRoot);

        while (pending.Count > 0)
        {
            PendingDirectory current = pending.Dequeue();
            string directory = current.Path;
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch (Exception exception) when (IsFileSystemAccessException(exception))
            {
                continue;
            }

            foreach (string entry in entries)
            {
                if (inspectedEntries >= MaxInspectedEntries ||
                    restricted.Count >= MaxRestrictedPaths)
                {
                    truncated = true;
                    break;
                }

                inspectedEntries++;

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (IsFileSystemAccessException(exception))
                {
                    continue;
                }

                bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
                bool isLink = attributes.HasFlag(FileAttributes.ReparsePoint);

                if (ignoreMatcher.TryGetIgnoreSource(entry, isDirectory, out string sourceDisplayPath))
                {
                    // The entry itself is restricted, so stop descending: masking the subtree
                    // root already covers everything beneath it.
                    restricted.Add(new WorkspaceRestrictedPath(
                        Path.GetFullPath(entry),
                        isDirectory,
                        WorkspacePath.ToRelativePath(workspaceRoot, entry),
                        sourceDisplayPath));
                    continue;
                }

                if (!isDirectory)
                {
                    continue;
                }

                if (current.Depth >= MaxDirectoryDepth)
                {
                    truncated = true;
                    break;
                }

                string canonicalEntry = CanonicalOrOriginal(entry);
                if (isLink && !CanFollowDirectoryLink(workspaceRoot, canonicalEntry))
                {
                    continue;
                }

                if (visitedDirectories.Add(canonicalEntry))
                {
                    pending.Enqueue(new PendingDirectory(canonicalEntry, current.Depth + 1));
                }
            }

            if (truncated)
            {
                break;
            }
        }

        StringComparer pathComparer = WorkspacePath.GetPathComparer();
        IReadOnlyList<WorkspaceRestrictedPath> ordered = [.. restricted
            .DistinctBy(static item => item.FullPath, pathComparer)
            .OrderBy(static item => item.RelativePath.Count(static character => character == '/'))
            .ThenBy(static item => item.RelativePath, pathComparer)];

        return new WorkspaceRestrictedPathPolicy(
            workspaceRoot,
            ignoreMatcher,
            ordered,
            truncated);
    }

    private static string ReadIgnoreFileStamp(string workspaceRoot)
    {
        string ignoreFilePath = Path.Combine(
            workspaceRoot,
            WorkspaceIgnoreMatcher.StemCodeIgnoreRelativePath);

        try
        {
            FileInfo info = new(ignoreFilePath);
            return info.Exists
                ? string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{info.Length}:{info.LastWriteTimeUtc.Ticks}")
                : "missing";
        }
        catch (Exception exception) when (IsFileSystemAccessException(exception))
        {
            return "unreadable";
        }
    }

    private static bool IsPathException(Exception exception)
    {
        return exception is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            IOException or
            System.Security.SecurityException;
    }

    /// <summary>
    /// Resolves symlinks and reparse points to the final target, falling back to the canonical
    /// path when the link cannot be resolved.
    /// </summary>
    internal static string ResolveRealPath(string path)
    {
        string canonical;
        try
        {
            canonical = Path.GetFullPath(path);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return path;
        }

        try
        {
            FileSystemInfo? target = Directory.Exists(canonical)
                ? Directory.ResolveLinkTarget(canonical, returnFinalTarget: true)
                : File.ResolveLinkTarget(canonical, returnFinalTarget: true);
            return target is null ? canonical : Path.GetFullPath(target.FullName);
        }
        catch (Exception exception) when (
            IsPathException(exception) || exception is UnauthorizedAccessException)
        {
            return canonical;
        }
    }

    private static bool IsSamePathOrDescendantSafe(string parentPath, string candidatePath)
    {
        try
        {
            return WorkspacePath.IsSamePathOrDescendant(parentPath, candidatePath);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a symlinked directory may be traversed. Links are followed only when their target
    /// stays inside the workspace and does not point at the link's own ancestry, which would
    /// create a cycle.
    /// </summary>
    private static bool CanFollowDirectoryLink(string workspaceRoot, string canonicalLinkPath)
    {
        string realTarget = ResolveRealPath(canonicalLinkPath);
        if (!IsSamePathOrDescendantSafe(workspaceRoot, realTarget))
        {
            return false;
        }

        return !IsSamePathOrDescendantSafe(realTarget, canonicalLinkPath);
    }

    private static string CanonicalOrOriginal(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return path;
        }
    }

    private static bool IsFileSystemAccessException(Exception exception)
    {
        return exception is UnauthorizedAccessException or
            IOException or
            PathTooLongException or
            ArgumentException or
            System.Security.SecurityException;
    }

    private sealed record CacheEntry(
        WorkspaceRestrictedPathPolicy Policy,
        string IgnoreStamp,
        DateTimeOffset ResolvedAt);

    private readonly record struct PendingDirectory(string Path, int Depth);
}
