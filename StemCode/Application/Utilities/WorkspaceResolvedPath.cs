using StemCode.Application.Models;

namespace StemCode.Application.Utilities;

internal static class WorkspaceResolvedPath
{
    internal sealed record Resolution(
        string WorkspaceRootPath,
        string CanonicalWorkspaceRootPath,
        string RequestedFullPath,
        string CanonicalFullPath,
        bool Exists,
        bool TraversedReparsePoint);

    public static Resolution Resolve(
        string workspaceRoot,
        string? requestedPath,
        ToolPathAccessKind accessKind)
    {
        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        string requestedFullPath = WorkspacePath.Resolve(fullWorkspaceRoot, requestedPath);
        string canonicalWorkspaceRoot = ResolveExistingPath(fullWorkspaceRoot);
        string relativePath = Path.GetRelativePath(fullWorkspaceRoot, requestedFullPath);

        if (string.IsNullOrWhiteSpace(relativePath) ||
            string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            return new Resolution(
                fullWorkspaceRoot,
                canonicalWorkspaceRoot,
                requestedFullPath,
                canonicalWorkspaceRoot,
                Exists: true,
                TraversedReparsePoint: false);
        }

        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        string currentCanonicalPath = canonicalWorkspaceRoot;
        string currentRequestedPath = fullWorkspaceRoot;
        bool traversedReparsePoint = false;

        for (int index = 0; index < segments.Length; index++)
        {
            currentRequestedPath = Path.Combine(currentRequestedPath, segments[index]);
            if (!TryResolveExistingPath(
                    currentRequestedPath,
                    out string resolvedPath,
                    out bool isReparsePoint))
            {
                string plannedPath = AppendRemainingSegments(
                    currentCanonicalPath,
                    segments,
                    index);
                EnsureWorkspaceDescendant(canonicalWorkspaceRoot, plannedPath);
                EnsureWritePathDoesNotTraverseReparsePoints(accessKind, traversedReparsePoint);
                return new Resolution(
                    fullWorkspaceRoot,
                    canonicalWorkspaceRoot,
                    requestedFullPath,
                    plannedPath,
                    Exists: false,
                    TraversedReparsePoint: traversedReparsePoint);
            }

            EnsureWorkspaceDescendant(canonicalWorkspaceRoot, resolvedPath);
            traversedReparsePoint |= isReparsePoint;
            EnsureWritePathDoesNotTraverseReparsePoints(accessKind, traversedReparsePoint);
            currentCanonicalPath = resolvedPath;
        }

        return new Resolution(
            fullWorkspaceRoot,
            canonicalWorkspaceRoot,
            requestedFullPath,
            currentCanonicalPath,
            Exists: true,
            TraversedReparsePoint: traversedReparsePoint);
    }

    public static void EnsurePathStaysWithinWorkspace(
        string workspaceRoot,
        string fullPath)
    {
        _ = Resolve(workspaceRoot, fullPath, ToolPathAccessKind.Read);
    }

    public static void Revalidate(
        string workspaceRoot,
        string fullPath,
        string expectedCanonicalFullPath,
        ToolPathAccessKind accessKind)
    {
        Resolution resolution = Resolve(workspaceRoot, fullPath, accessKind);
        if (!WorkspacePath.PathEquals(resolution.CanonicalFullPath, expectedCanonicalFullPath))
        {
            throw new InvalidOperationException(
                "Tool path validation failed because the target changed during access.");
        }
    }

    private static string ResolveExistingPath(string path)
    {
        return TryResolveExistingPath(path, out string resolvedPath, out _)
            ? resolvedPath
            : Path.GetFullPath(path);
    }

    private static bool TryResolveExistingPath(
        string path,
        out string resolvedPath,
        out bool isReparsePoint)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            FileSystemInfo fileSystemInfo = attributes.HasFlag(FileAttributes.Directory)
                ? new DirectoryInfo(path)
                : new FileInfo(path);

            isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);
            if (isReparsePoint)
            {
                FileSystemInfo? target = fileSystemInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null)
                {
                    throw new InvalidOperationException(
                        "Tool paths cannot use unresolved symbolic links or reparse points.");
                }

                resolvedPath = Path.GetFullPath(target.FullName);
                return true;
            }

            resolvedPath = Path.GetFullPath(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            resolvedPath = Path.GetFullPath(path);
            isReparsePoint = false;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            resolvedPath = Path.GetFullPath(path);
            isReparsePoint = false;
            return false;
        }
    }

    private static void EnsureWritePathDoesNotTraverseReparsePoints(
        ToolPathAccessKind accessKind,
        bool traversedReparsePoint)
    {
        if (accessKind == ToolPathAccessKind.Write && traversedReparsePoint)
        {
            throw new InvalidOperationException(
                "Tool writes cannot traverse symbolic links, junctions, or reparse points.");
        }
    }

    private static string AppendRemainingSegments(
        string path,
        IReadOnlyList<string> segments,
        int currentIndex)
    {
        string currentPath = path;
        for (int index = currentIndex; index < segments.Count; index++)
        {
            currentPath = Path.Combine(currentPath, segments[index]);
        }

        return Path.GetFullPath(currentPath);
    }

    private static void EnsureWorkspaceDescendant(
        string workspaceRoot,
        string fullPath)
    {
        if (!WorkspacePath.IsSamePathOrDescendant(workspaceRoot, fullPath))
        {
            throw new InvalidOperationException(
                "Tool paths must stay within the current workspace.");
        }
    }
}
