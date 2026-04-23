namespace NanoAgent.Application.Utilities;

internal static class WorkspacePath
{
    public static bool IsSamePathOrDescendant(
        string parentPath,
        string candidatePath)
    {
        string fullParentPath = Path.GetFullPath(parentPath);
        string fullCandidatePath = Path.GetFullPath(candidatePath);
        StringComparison comparison = GetPathComparison();

        string normalizedParent = EnsureTrailingSeparator(fullParentPath);
        string normalizedCandidate = EnsureTrailingSeparator(fullCandidatePath);

        return normalizedCandidate.StartsWith(normalizedParent, comparison) ||
               string.Equals(fullParentPath, fullCandidatePath, comparison);
    }

    public static bool PathEquals(
        string leftPath,
        string rightPath)
    {
        return string.Equals(
            Path.GetFullPath(leftPath),
            Path.GetFullPath(rightPath),
            GetPathComparison());
    }

    public static string Resolve(
        string workspaceRoot,
        string? requestedPath)
    {
        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        string normalizedRequestedPath = string.IsNullOrWhiteSpace(requestedPath)
            ? fullWorkspaceRoot
            : requestedPath.Trim();

        string fullPath = Path.GetFullPath(
            Path.IsPathRooted(normalizedRequestedPath)
                ? normalizedRequestedPath
                : Path.Combine(fullWorkspaceRoot, normalizedRequestedPath));

        if (!IsSamePathOrDescendant(fullWorkspaceRoot, fullPath))
        {
            throw new InvalidOperationException(
                "Tool paths must stay within the current workspace.");
        }

        return fullPath;
    }

    public static string ToRelativePath(
        string workspaceRoot,
        string fullPath)
    {
        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        string normalizedFullPath = Path.GetFullPath(fullPath);

        if (string.Equals(fullWorkspaceRoot, normalizedFullPath, GetPathComparison()))
        {
            return ".";
        }

        return Path.GetRelativePath(fullWorkspaceRoot, normalizedFullPath)
            .Replace('\\', '/');
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ||
               path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
