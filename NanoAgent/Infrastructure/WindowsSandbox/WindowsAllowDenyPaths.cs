using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal sealed record WindowsAllowDenyPaths(
    HashSet<string> Allow,
    HashSet<string> Deny);

internal static class WindowsAllowDenyPlanner
{
    private static readonly string[] ProtectedChildren = [".git", ".nanoagent", ".agents"];

    public static WindowsAllowDenyPaths Compute(
        ToolSandboxMode mode,
        string policyCwd,
        string commandCwd,
        IEnumerable<string> writableRoots,
        IReadOnlyDictionary<string, string>? environment,
        bool includeTempEnvironmentVariables)
    {
        HashSet<string> allow = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> deny = new(StringComparer.OrdinalIgnoreCase);

        if (mode == ToolSandboxMode.WorkspaceWrite)
        {
            AddWritableRoot(commandCwd, policyCwd, allow, deny);
            foreach (string root in writableRoots)
            {
                AddWritableRoot(root, policyCwd, allow, deny);
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

        return new WindowsAllowDenyPaths(allow, deny);
    }

    private static void AddWritableRoot(
        string root,
        string policyCwd,
        HashSet<string> allow,
        HashSet<string> deny)
    {
        string candidate = Path.IsPathRooted(root)
            ? root
            : Path.Combine(policyCwd, root);
        string? canonical = AddExistingPath(candidate, allow);
        if (canonical is null)
        {
            return;
        }

        foreach (string child in ProtectedChildren)
        {
            AddExistingPath(Path.Combine(canonical, child), deny);
        }
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
