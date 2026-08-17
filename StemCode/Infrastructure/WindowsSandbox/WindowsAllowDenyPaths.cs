using StemCode.Application.Models;
using StemCode.Application.Utilities;
using StemCode.Infrastructure.Workspaces;

namespace StemCode.Infrastructure.WindowsSandbox;

internal sealed record WindowsAllowDenyPaths(
    HashSet<string> Allow,
    HashSet<string> Deny,
    HashSet<string> DenyRead);

internal static class WindowsAllowDenyPlanner
{
    public static WindowsAllowDenyPaths Compute(
        ToolSandboxMode mode,
        string policyCwd,
        string commandCwd,
        IEnumerable<string> writableRoots,
        IReadOnlyDictionary<string, string>? environment,
        bool includeTempEnvironmentVariables,
        WorkspaceRestrictedPathPolicy? restrictedPathPolicy = null)
    {
        HashSet<string> allow = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> deny = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> denyRead = new(StringComparer.OrdinalIgnoreCase);
        string[] materializedWritableRoots = [.. writableRoots];

        if (mode == ToolSandboxMode.WorkspaceWrite)
        {
            AddWritableRoot(commandCwd, policyCwd, allow);
            foreach (string root in materializedWritableRoots)
            {
                AddWritableRoot(root, policyCwd, allow);
            }
        }

        if (mode == ToolSandboxMode.WorkspaceWrite && includeTempEnvironmentVariables)
        {
            foreach (string key in new[] { "TEMP", "TMP" })
            {
                string? value = null;
                if (environment is not null)
                {
                    environment.TryGetValue(key, out value);
                }

                value ??= Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    AddExistingPath(value, allow);
                }
            }
        }

        AddRestrictedPaths(
            restrictedPathPolicy,
            policyCwd,
            commandCwd,
            materializedWritableRoots,
            deny,
            denyRead);

        return new WindowsAllowDenyPaths(allow, deny, denyRead);
    }

    /// <summary>
    /// Derives the denied paths from the workspace restriction policy.
    /// </summary>
    /// <remarks>
    /// <c>.stemcode/.stemcodeignore</c> is the only source of denials. A path the sandbox could
    /// technically restrict but that the policy does not match is intentionally left accessible,
    /// which keeps OS enforcement and StemCode's own file tools in agreement.
    /// </remarks>
    private static void AddRestrictedPaths(
        WorkspaceRestrictedPathPolicy? restrictedPathPolicy,
        string policyCwd,
        string commandCwd,
        IReadOnlyList<string> writableRoots,
        HashSet<string> deny,
        HashSet<string> denyRead)
    {
        if (restrictedPathPolicy is null || !restrictedPathPolicy.HasRestrictions)
        {
            return;
        }

        // An incomplete policy would produce ACLs that only look like full enforcement.
        if (restrictedPathPolicy.Truncated)
        {
            throw new InvalidOperationException(
                "The workspace read restriction policy could not be fully resolved because the workspace exceeded the scan limits. " +
                "Narrow the patterns in .stemcode/.stemcodeignore so the restricted paths can be enforced by the Windows sandbox.");
        }

        List<string> scopes = [commandCwd, policyCwd, .. writableRoots];

        foreach (WorkspaceRestrictedPath restricted in restrictedPathPolicy.RestrictedPaths)
        {
            bool inScope = scopes.Any(scope =>
                !string.IsNullOrWhiteSpace(scope) &&
                IsSamePathOrDescendantSafe(scope, restricted.FullPath));
            if (!inScope)
            {
                continue;
            }

            // Never deny the roots themselves; that would make the sandbox unusable and is
            // never what an ignore rule expresses.
            if (scopes.Any(scope =>
                    !string.IsNullOrWhiteSpace(scope) &&
                    PathEqualsSafe(scope, restricted.FullPath)))
            {
                continue;
            }

            string? canonical = AddExistingPath(restricted.FullPath, denyRead);
            if (canonical is not null)
            {
                deny.Add(canonical);
            }
        }
    }

    private static void AddWritableRoot(
        string root,
        string policyCwd,
        HashSet<string> allow)
    {
        string candidate = Path.IsPathRooted(root)
            ? root
            : Path.Combine(policyCwd, root);
        AddExistingPath(candidate, allow);
    }

    private static string? AddExistingPath(string path, HashSet<string> set)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        string canonical = Canonicalize(path);
        set.Add(canonical);
        return canonical;
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

    private static bool PathEqualsSafe(string leftPath, string rightPath)
    {
        try
        {
            return WorkspacePath.PathEquals(leftPath, rightPath);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return false;
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

    private static string Canonicalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }
}
