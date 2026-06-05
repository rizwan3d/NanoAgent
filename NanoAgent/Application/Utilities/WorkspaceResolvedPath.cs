namespace NanoAgent.Application.Utilities;

internal static class WorkspaceResolvedPath
{
    public static void EnsurePathStaysWithinWorkspace(
        string workspaceRoot,
        string fullPath)
    {
        string resolvedWorkspaceRoot = ResolveExistingPath(workspaceRoot);
        string relativePath = Path.GetRelativePath(workspaceRoot, fullPath);
        if (string.IsNullOrWhiteSpace(relativePath) ||
            string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            return;
        }

        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        string currentPath = resolvedWorkspaceRoot;
        for (int index = 0; index < segments.Length; index++)
        {
            currentPath = Path.Combine(currentPath, segments[index]);
            if (!TryResolveExistingPath(currentPath, out string resolvedPath))
            {
                string plannedPath = AppendRemainingSegments(currentPath, segments, index + 1);
                EnsureWorkspaceDescendant(resolvedWorkspaceRoot, plannedPath);
                return;
            }

            EnsureWorkspaceDescendant(resolvedWorkspaceRoot, resolvedPath);
            currentPath = resolvedPath;
        }
    }

    private static string ResolveExistingPath(string path)
    {
        return TryResolveExistingPath(path, out string resolvedPath)
            ? resolvedPath
            : Path.GetFullPath(path);
    }

    private static bool TryResolveExistingPath(
        string path,
        out string resolvedPath)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            FileSystemInfo fileSystemInfo = attributes.HasFlag(FileAttributes.Directory)
                ? new DirectoryInfo(path)
                : new FileInfo(path);

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
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
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            resolvedPath = Path.GetFullPath(path);
            return false;
        }
    }

    private static string AppendRemainingSegments(
        string path,
        IReadOnlyList<string> segments,
        int startIndex)
    {
        string currentPath = path;
        for (int index = startIndex; index < segments.Count; index++)
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
