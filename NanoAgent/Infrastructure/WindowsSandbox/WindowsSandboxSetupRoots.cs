using NanoAgent.Application.Models;
using System.Runtime.InteropServices;
using System.Text;

namespace NanoAgent.Infrastructure.WindowsSandbox;

/// <summary>
/// Port of the Windows sandbox root-gathering, profile-expansion, filtering,
/// and proxy-port-detection logic from the Rust windows-sandbox-rs/src/setup.rs,
/// allow.rs, ssh_config_dependencies.rs, and path_normalization.rs.
/// All members are internal and aggregated into WindowsSandboxSetupOrchestrator.
/// </summary>
internal static class WindowsSandboxSetupRoots
{
    // â”€â”€ Constants ported from Rust setup.rs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Platform default read roots that every sandboxed process can read/execute.
    /// Ported from <c>WINDOWS_PLATFORM_DEFAULT_READ_ROOTS</c>.
    /// </summary>
    internal static readonly string[] PlatformDefaultReadRoots =
    [
        @"C:\Windows",
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\ProgramData",
    ];

    /// <summary>
    /// Top-level user-profile entries excluded from read-root expansion.
    /// Ported from <c>USERPROFILE_ROOT_EXCLUSIONS</c>.
    /// </summary>
    private static readonly string[] UserProfileRootExclusions =
    [
        ".ssh",
        ".tsh",
        ".brev",
        ".gnupg",
        ".aws",
        ".azure",
        ".kube",
        ".docker",
        ".config",
        ".npm",
        ".pki",
        ".terraform.d",
    ];

    // â”€â”€ Proxy-related constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static readonly string[] ProxyEnvKeys =
    [
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "ALL_PROXY",
        "WS_PROXY",
        "WSS_PROXY",
        "http_proxy",
        "https_proxy",
        "all_proxy",
        "ws_proxy",
        "wss_proxy",
    ];

    private const string AllowLocalBindingEnvKey = "CODEX_NETWORK_ALLOW_LOCAL_BINDING";

    // â”€â”€ SSH config directive constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static readonly string[] SshProfilePathDirectives =
    [
        "certificatefile",
        "controlpath",
        "globalknownhostsfile",
        "identityagent",
        "identityfile",
        "revokedhostkeys",
        "userknownhostsfile",
    ];

    // â”€â”€ Public entry points â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Build the read-roots and write-roots for a sandbox setup payload.
    /// Ported from <c>build_payload_roots()</c> in Rust setup.rs.
    /// </summary>
    internal static void BuildPayloadRoots(
        WindowsSandboxSetupPayload payload,
        ToolSandboxMode mode,
        string policyCwd,
        string commandCwd,
        string[] writableRoots,
        IReadOnlyDictionary<string, string>? envMap,
        bool includeTempEnvironmentVariables,
        string nanoAgentHome)
    {
        // â”€â”€ Write roots â”€â”€
        var writeRoots = GatherWriteRoots(mode, policyCwd, commandCwd, writableRoots, envMap, includeTempEnvironmentVariables, nanoAgentHome);
        writeRoots = ExpandUserProfileRoot(writeRoots);
        writeRoots = FilterUserProfileRoot(writeRoots);
        writeRoots = FilterUserProfileRootExclusions(writeRoots);
        writeRoots = FilterSshConfigDependencyRoots(writeRoots);
        writeRoots = FilterSensitiveWriteRoots(writeRoots, nanoAgentHome);

        // â”€â”€ Read roots â”€â”€
        var readRoots = GatherReadRoots(mode, commandCwd, writableRoots, nanoAgentHome);
        readRoots = ExpandUserProfileRoot(readRoots);
        readRoots = FilterUserProfileRoot(readRoots);
        readRoots = FilterUserProfileRootExclusions(readRoots);
        readRoots = FilterSshConfigDependencyRoots(readRoots);

        // Ensure read roots do not duplicate write roots
        var writeRootSet = new HashSet<string>(writeRoots, StringComparer.OrdinalIgnoreCase);
        readRoots.RemoveAll(root => writeRootSet.Contains(root));

        payload.ReadRoots = [.. readRoots];
        payload.WriteRoots = [.. writeRoots];
    }

    /// <summary>
    /// Build the deny-write-paths for a sandbox setup payload.
    /// Ported from <c>build_payload_deny_write_paths()</c> in Rust setup.rs.
    /// </summary>
    internal static string[] BuildPayloadDenyWritePaths(
        ToolSandboxMode mode,
        string policyCwd,
        string commandCwd,
        string[] writableRoots,
        IReadOnlyDictionary<string, string>? envMap,
        bool includeTempEnvironmentVariables,
        string[]? explicitDenyWritePaths)
    {
        var allowDeny = ComputeAllowDenyPaths(mode, policyCwd, commandCwd, writableRoots, envMap, includeTempEnvironmentVariables);
        var denyWritePaths = new List<string>();

        if (explicitDenyWritePaths is not null)
        {
            foreach (string path in explicitDenyWritePaths)
            {
                denyWritePaths.Add(
                    Directory.Exists(path) || File.Exists(path)
                        ? CanonicalExistingPath(path)
                        : path);
            }
        }

        denyWritePaths.AddRange(allowDeny.Deny);
        return [.. denyWritePaths];
    }

    // â”€â”€ Read-root gathering (ported from gather_read_roots / gather_legacy_full_read_roots) â”€â”€

    /// <summary>
    /// Gather all read roots for a sandbox session.
    /// Ported from <c>gather_read_roots()</c> and <c>gather_legacy_full_read_roots()</c>.
    /// </summary>
    internal static List<string> GatherReadRoots(
        ToolSandboxMode mode,
        string commandCwd,
        string[] writableRoots,
        string nanoAgentHome)
    {
        var roots = new List<string>();

        // Helper binary directory
        roots.Add(WindowsSandboxPaths.SandboxBinDir(nanoAgentHome));

        // Platform defaults
        foreach (string platformRoot in PlatformDefaultReadRoots)
        {
            roots.Add(platformRoot);
        }

        // User profile children (excluding sensitive entries)
        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            roots.AddRange(ProfileReadRoots(userProfile));
        }

        // Command working directory
        roots.Add(commandCwd);

        // Writable roots from policy
        foreach (string root in writableRoots)
        {
            roots.Add(root);
        }

        return CanonicalExistingOnly(roots);
    }

    /// <summary>
    /// Gather write roots for a sandbox session.
    /// Ported from <c>gather_write_roots()</c>.
    /// </summary>
    internal static List<string> GatherWriteRoots(
        ToolSandboxMode mode,
        string policyCwd,
        string commandCwd,
        string[] writableRoots,
        IReadOnlyDictionary<string, string>? envMap,
        bool includeTempEnvironmentVariables,
        string nanoAgentHome)
    {
        var roots = new List<string>();

        if (mode == ToolSandboxMode.WorkspaceWrite)
        {
            roots.Add(commandCwd);

            foreach (string root in writableRoots)
            {
                string candidate = Path.IsPathRooted(root)
                    ? root
                    : Path.Combine(policyCwd, root);
                roots.Add(candidate);
            }
        }

        // Add allowed paths from allow/deny computation
        var allowDeny = ComputeAllowDenyPaths(mode, policyCwd, commandCwd, writableRoots, envMap, includeTempEnvironmentVariables);
        roots.AddRange(allowDeny.Allow);

        // Deduplicate and canonicalize
        var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string root in CanonicalExistingOnly(roots))
        {
            if (dedup.Add(root))
            {
                result.Add(root);
            }
        }

        return result;
    }

    // â”€â”€ Allow/deny path computation (ported from Rust allow.rs) â”€â”€

    /// <summary>
    /// Compute allowed (writable) and denied (deny-write) paths.
    /// Ported from <c>compute_allow_paths()</c> in Rust allow.rs.
    /// </summary>
    internal static (HashSet<string> Allow, HashSet<string> Deny) ComputeAllowDenyPaths(
        ToolSandboxMode mode,
        string policyCwd,
        string commandCwd,
        string[] writableRoots,
        IReadOnlyDictionary<string, string>? envMap,
        bool includeTempEnvironmentVariables)
    {
        var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deny = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool addExistingAllowPath(string p)
        {
            if (Directory.Exists(p) || File.Exists(p))
            {
                string canonical = CanonicalExistingPath(p);
                allow.Add(canonical);
                return true;
            }
            return false;
        }

        void addExistingDenyPath(string p)
        {
            if (Directory.Exists(p) || File.Exists(p))
            {
                deny.Add(CanonicalExistingPath(p));
            }
        }

        string[] ProtectedChildren = [".git", ".codex", ".nanoagent", ".agents"];

        void addWritableRoot(string root, string policyCwdLocal)
        {
            string candidate = Path.IsPathRooted(root)
                ? root
                : Path.Combine(policyCwdLocal, root);
            string? canonical = null;
            if (addExistingAllowPath(candidate))
            {
                canonical = CanonicalExistingPath(candidate);
            }
            else
            {
                try
                {
                    canonical = Path.GetFullPath(candidate);
                }
                catch
                {
                    return;
                }
            }

            if (canonical is not null)
            {
                foreach (string child in ProtectedChildren)
                {
                    string protectedEntry = Path.Combine(canonical, child);
                    addExistingDenyPath(protectedEntry);
                }
            }
        }

        if (mode == ToolSandboxMode.WorkspaceWrite)
        {
            addWritableRoot(commandCwd, policyCwd);
            foreach (string root in writableRoots)
            {
                addWritableRoot(root, policyCwd);
            }

            if (includeTempEnvironmentVariables)
            {
                foreach (string key in new[] { "TEMP", "TMP" })
                {
                    string? value = null;
                    if (envMap is not null)
                    {
                        envMap.TryGetValue(key, out value);
                    }
                    value ??= Environment.GetEnvironmentVariable(key);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        addExistingAllowPath(value);
                    }
                }
            }
        }

        return (allow, deny);
    }

    // â”€â”€ Profile read-root helpers â”€â”€

    /// <summary>
    /// Enumerate the top-level children of a user profile directory,
    /// skipping entries in <see cref="UserProfileRootExclusions"/>.
    /// Ported from <c>profile_read_roots()</c>.
    /// </summary>
    internal static List<string> ProfileReadRoots(string userProfile)
    {
        var result = new List<string>();

        if (!Directory.Exists(userProfile))
        {
            result.Add(userProfile);
            return result;
        }

        try
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(userProfile))
            {
                string name = Path.GetFileName(entry);
                if (!UserProfileRootExclusions.Any(excluded =>
                        name.Equals(excluded, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(entry);
                }
            }
        }
        catch
        {
            result.Add(userProfile);
        }

        return result;
    }

    // â”€â”€ Canonical path key (ported from path_normalization.rs) â”€â”€

    /// <summary>
    /// Ported from <c>canonical_path_key()</c>. Normalizes a path to a
    /// lowercase, forward-slash-separated key for comparison and deduplication.
    /// </summary>
    internal static string CanonicalPathKey(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            full = path;
        }

        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return full.ToLowerInvariant();
    }

    /// <summary>
    /// Canonicalize and keep only existing paths. Ported from <c>canonical_existing()</c>.
    /// </summary>
    internal static List<string> CanonicalExistingOnly(List<string> paths)
    {
        var result = new List<string>(paths.Count);
        foreach (string p in paths)
        {
            if (!Directory.Exists(p) && !File.Exists(p))
            {
                continue;
            }
            result.Add(CanonicalExistingPath(p));
        }
        return result;
    }

    /// <summary>
    /// Resolve a path to its canonical (real) form if it exists, else return as-is.
    /// </summary>
    internal static string CanonicalExistingPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    // â”€â”€ User profile root expansion/filtering (ported from setup.rs) â”€â”€

    /// <summary>
    /// When a root path IS the user profile root, replace it with its children.
    /// Ported from <c>expand_user_profile_root()</c>.
    /// </summary>
    internal static List<string> ExpandUserProfileRoot(List<string> roots)
    {
        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return roots;
        }
        return ExpandUserProfileRootFor(roots, userProfile);
    }

    internal static List<string> ExpandUserProfileRootFor(List<string> roots, string userProfile)
    {
        string profileKey = CanonicalPathKey(userProfile);
        var expanded = new List<string>(roots.Count);
        foreach (string root in roots)
        {
            if (string.Equals(CanonicalPathKey(root), profileKey, StringComparison.OrdinalIgnoreCase))
            {
                expanded.AddRange(ProfileReadRoots(userProfile));
            }
            else
            {
                expanded.Add(root);
            }
        }

        // Deduplicate by canonical path key
        expanded = expanded
            .GroupBy(r => CanonicalPathKey(r), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return expanded;
    }

    /// <summary>
    /// Remove entries that exactly match the user profile root.
    /// Ported from <c>filter_user_profile_root()</c>.
    /// </summary>
    internal static List<string> FilterUserProfileRoot(List<string> roots)
    {
        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return roots;
        }
        string profileKey = CanonicalPathKey(userProfile);
        roots.RemoveAll(root => string.Equals(CanonicalPathKey(root), profileKey, StringComparison.OrdinalIgnoreCase));
        return roots;
    }

    /// <summary>
    /// Remove entries that match configured exclusion patterns under the user profile.
    /// Ported from <c>filter_user_profile_root_exclusions()</c>.
    /// </summary>
    internal static List<string> FilterUserProfileRootExclusions(List<string> roots)
    {
        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return roots;
        }
        roots.RemoveAll(root => IsUserProfileRootExclusion(root, userProfile));
        return roots;
    }

    /// <summary>
    /// Ported from <c>is_user_profile_root_exclusion()</c>.
    /// </summary>
    internal static bool IsUserProfileRootExclusion(string root, string userProfile)
    {
        string rootKey = CanonicalPathKey(root);
        string profileKey = CanonicalPathKey(userProfile);
        string profilePrefix = profileKey.TrimEnd('/') + "/";

        if (!rootKey.StartsWith(profilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string relativeKey = rootKey[profilePrefix.Length..];
        string? childName = relativeKey.Split('/').FirstOrDefault();
        if (string.IsNullOrEmpty(childName))
        {
            return false;
        }

        return UserProfileRootExclusions.Any(excluded =>
            childName.Equals(excluded, StringComparison.OrdinalIgnoreCase));
    }

    // â”€â”€ SSH config dependency filtering (ported from ssh_config_dependencies.rs) â”€â”€

    /// <summary>
    /// Remove entries that match SSH config dependency paths.
    /// Ported from <c>filter_ssh_config_dependency_roots()</c>.
    /// </summary>
    internal static List<string> FilterSshConfigDependencyRoots(List<string> roots)
    {
        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return roots;
        }

        var dependencyPaths = SshConfigDependencyPaths(userProfile);
        roots.RemoveAll(root => IsSshConfigDependencyRoot(root, userProfile, dependencyPaths));
        return roots;
    }

    /// <summary>
    /// Ported from <c>is_ssh_config_dependency_root()</c>.
    /// </summary>
    internal static bool IsSshConfigDependencyRoot(string root, string userProfile, List<string> dependencyPaths)
    {
        string? childName = UserProfileChildName(root, userProfile);
        if (childName is null)
        {
            return false;
        }

        return dependencyPaths.Any(path =>
        {
            string? depChild = UserProfileChildName(path, userProfile);
            return depChild is not null &&
                   childName.Equals(depChild, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Ported from <c>user_profile_child_name()</c>.
    /// </summary>
    internal static string? UserProfileChildName(string path, string userProfile)
    {
        string rootKey = CanonicalPathKey(path);
        string profileKey = CanonicalPathKey(userProfile);
        string profilePrefix = profileKey.TrimEnd('/') + "/";

        if (!rootKey.StartsWith(profilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string relativeKey = rootKey[profilePrefix.Length..];
        string? childName = relativeKey.Split('/').FirstOrDefault();
        return string.IsNullOrEmpty(childName) ? null : childName;
    }

    /// <summary>
    /// Ported from <c>ssh_config_dependency_paths()</c>.
    /// Reads .ssh/config and traverses Include and path directives to collect
    /// dependent files.
    /// </summary>
    internal static List<string> SshConfigDependencyPaths(string userProfile)
    {
        string sshConfig = Path.Combine(userProfile, ".ssh", "config");
        var paths = new List<string> { sshConfig };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VisitSshConfig(sshConfig, userProfile, visited, paths, depth: 0);
        return paths;
    }

    private static void VisitSshConfig(string path, string userProfile, HashSet<string> visited, List<string> paths, int depth)
    {
        if (depth >= 32)
            return;

        string? resolved;
        try
        {
            resolved = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (!visited.Add(resolved))
            return;

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch
        {
            return;
        }

        foreach (var (key, args) in ParseSshDirectives(content))
        {
            string keyLower = key.ToLowerInvariant();
            if (keyLower == "include")
            {
                foreach (string arg in args)
                {
                    foreach (string include in SshIncludePaths(arg, userProfile))
                    {
                        paths.Add(include);
                        VisitSshConfig(include, userProfile, visited, paths, depth + 1);
                    }
                }
            }
            else if (SshProfilePathDirectives.Contains(keyLower))
            {
                foreach (string arg in args)
                {
                    string? profilePath = SshProfilePathArg(arg, userProfile, relativeBase: null);
                    if (profilePath is not null)
                    {
                        paths.Add(profilePath);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> SshIncludePaths(string arg, string userProfile)
    {
        string? patternPath = SshProfilePathArg(arg, userProfile, relativeBase: Path.Combine(userProfile, ".ssh"));
        if (patternPath is null)
            yield break;

        string pattern = patternPath.Replace('\\', '/');
        string? dir = Path.GetDirectoryName(pattern.Replace('/', Path.DirectorySeparatorChar));
        string searchPattern = Path.GetFileName(pattern.Replace('/', Path.DirectorySeparatorChar));
        foreach (string match in SshGlobFiles(dir, searchPattern))
        {
            yield return match;
        }
    }

    private static IEnumerable<string> SshGlobFiles(string? dir, string searchPattern)
    {
        if (dir is null || !Directory.Exists(dir))
        {
            yield break;
        }

        foreach (string match in Directory.EnumerateFiles(dir, searchPattern))
        {
            yield return match;
        }
    }
    private static IEnumerable<(string Key, List<string> Args)> ParseSshDirectives(string content)
    {
        foreach (string line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed[0] == '#')
                continue;

            var words = SshWordSplit(trimmed);
            if (words.Count == 0)
                continue;

            string first = words[0];

            // Check for key=value form
            int eqIndex = first.IndexOf('=');
            if (eqIndex > 0)
            {
                string key = first[..eqIndex];
                string value = first[(eqIndex + 1)..];
                var args = new List<string>();
                if (value.Length > 0)
                    args.Add(value);
                args.AddRange(words.GetRange(1, words.Count - 1));
                yield return (key, args);
            }
            else
            {
                string key = words[0];
                var args = words.GetRange(1, words.Count - 1);

                // Handle '=' prefix in first arg
                if (args.Count > 0 && args[0].Length > 0 && args[0][0] == '=')
                {
                    args[0] = args[0][1..];
                }
                args.RemoveAll(a => a.Length == 0);
                yield return (key, args);
            }
        }
    }

    private static List<string> SshWordSplit(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        char? quote = null;
        int i = 0;

        while (i < line.Length)
        {
            char ch = line[i];
            if (ch == '\'' || ch == '"')
            {
                if (quote == ch)
                {
                    quote = null;
                }
                else if (quote is null)
                {
                    quote = ch;
                }
                else
                {
                    word.Append(ch);
                }
            }
            else if (ch == '\\' && i + 1 < line.Length)
            {
                char next = line[i + 1];
                if (next is '\'' or '"' or '\\' || (quote is null && next == ' '))
                {
                    word.Append(next);
                    i++; // skip escaped char
                }
                else
                {
                    word.Append(ch);
                }
            }
            else if (char.IsWhiteSpace(ch) && quote is null)
            {
                if (word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }
            }
            else
            {
                word.Append(ch);
            }
            i++;
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    private static string? SshProfilePathArg(string arg, string userProfile, string? relativeBase)
    {
        if (arg.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        if (arg == "~" || arg == "%d" || arg == "${HOME}")
            return userProfile;

        // ~/ prefix
        if (arg.StartsWith("~/", StringComparison.Ordinal) ||
            arg.StartsWith(@"~\", StringComparison.Ordinal))
        {
            return Path.Combine(userProfile, arg[2..]);
        }

        // %d/ prefix
        if (arg.StartsWith("%d/", StringComparison.Ordinal) ||
            arg.StartsWith(@"%d\", StringComparison.Ordinal))
        {
            return Path.Combine(userProfile, arg[3..]);
        }

        // ${HOME}/ prefix
        if (arg.StartsWith("${HOME}/", StringComparison.Ordinal) ||
            arg.StartsWith(@"${HOME}\", StringComparison.Ordinal))
        {
            return Path.Combine(userProfile, arg[8..]);
        }

        if (Path.IsPathRooted(arg))
            return arg;

        if (relativeBase is not null)
            return Path.Combine(relativeBase, arg);

        return null;
    }

    // â”€â”€ Sensitive write root filtering (ported from filter_sensitive_write_roots) â”€â”€

    /// <summary>
    /// Never grant capability write access to nanoAgentHome, .sandbox, .sandbox-bin,
    /// or .sandbox-secrets directories.
    /// Ported from <c>filter_sensitive_write_roots()</c>.
    /// </summary>
    internal static List<string> FilterSensitiveWriteRoots(List<string> roots, string nanoAgentHome)
    {
        string homeKey = CanonicalPathKey(nanoAgentHome);
        string sandboxDir = WindowsSandboxPaths.SandboxDir(nanoAgentHome);
        string sandboxBinDir = WindowsSandboxPaths.SandboxBinDir(nanoAgentHome);
        string sandboxSecretsDir = WindowsSandboxPaths.SandboxSecretsDir(nanoAgentHome);

        string sbxKey = CanonicalPathKey(sandboxDir);
        string sbxPrefix = sbxKey.TrimEnd('/') + "/";
        string sbxBinKey = CanonicalPathKey(sandboxBinDir);
        string sbxBinPrefix = sbxBinKey.TrimEnd('/') + "/";
        string secretsKey = CanonicalPathKey(sandboxSecretsDir);
        string secretsPrefix = secretsKey.TrimEnd('/') + "/";

        roots.RemoveAll(root =>
        {
            string key = CanonicalPathKey(root);
            return key == homeKey ||
                   key == sbxKey ||
                   key.StartsWith(sbxPrefix, StringComparison.OrdinalIgnoreCase) ||
                   key == sbxBinKey ||
                   key.StartsWith(sbxBinPrefix, StringComparison.OrdinalIgnoreCase) ||
                   key == secretsKey ||
                   key.StartsWith(secretsPrefix, StringComparison.OrdinalIgnoreCase);
        });

        return roots;
    }

    // â”€â”€ Proxy / network-identity (ported from setup.rs) â”€â”€

    /// <summary>
    /// Ported from <c>SandboxNetworkIdentity</c>.
    /// </summary>
    internal enum SandboxNetworkIdentity
    {
        Offline,
        Online,
    }

    /// <summary>
    /// Ported from <c>OfflineProxySettings</c>.
    /// </summary>
    internal sealed record OfflineProxySettings(
        List<int> ProxyPorts,
        bool AllowLocalBinding);

    /// <summary>
    /// Determine network identity from policy and proxy-enforced flag.
    /// Ported from <c>SandboxNetworkIdentity::from_policy()</c>.
    /// </summary>
    internal static SandboxNetworkIdentity NetworkIdentityFromPolicy(
        ToolSandboxMode mode,
        bool proxyEnforced)
    {
        if (proxyEnforced)
            return SandboxNetworkIdentity.Offline;

        // In the C# codepath, only ReadOnly and WorkspaceWrite are supported;
        // both restrict network unless configured otherwise. We default to Offline
        // when network access isn't explicitly granted (matching Rust behavior).
        return SandboxNetworkIdentity.Offline;
    }

    /// <summary>
    /// Extract proxy settings from environment for offline identity.
    /// Ported from <c>offline_proxy_settings_from_env()</c>.
    /// </summary>
    internal static OfflineProxySettings OfflineProxySettingsFromEnv(
        IReadOnlyDictionary<string, string>? envMap,
        SandboxNetworkIdentity networkIdentity)
    {
        if (networkIdentity != SandboxNetworkIdentity.Offline)
        {
            return new OfflineProxySettings([], false);
        }

        return new OfflineProxySettings(
            ProxyPortsFromEnv(envMap),
            envMap is not null &&
            envMap.TryGetValue(AllowLocalBindingEnvKey, out string? bindingValue) &&
            bindingValue == "1");
    }

    /// <summary>
    /// Extract proxy ports from environment variables.
    /// Ported from <c>proxy_ports_from_env()</c>.
    /// </summary>
    internal static List<int> ProxyPortsFromEnv(IReadOnlyDictionary<string, string>? envMap)
    {
        var ports = new SortedSet<int>();
        if (envMap is null)
            return [];

        foreach (string key in ProxyEnvKeys)
        {
            if (envMap.TryGetValue(key, out string? value) &&
                LoopbackProxyPortFromUrl(value) is int port)
            {
                ports.Add(port);
            }
        }

        return [.. ports];
    }

    /// <summary>
    /// Extract a port number from a loopback proxy URL.
    /// Ported from <c>loopback_proxy_port_from_url()</c>.
    /// </summary>
    internal static int? LoopbackProxyPortFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // Split scheme
        string rest = url.Trim();
        int schemeEnd = rest.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            return null;

        string authority = rest[(schemeEnd + 3)..];
        // Split on first '/'
        int pathStart = authority.IndexOf('/');
        string hostPort = pathStart >= 0 ? authority[..pathStart] : authority;

        // Strip userinfo@
        int atIndex = hostPort.LastIndexOf('@');
        if (atIndex >= 0)
            hostPort = hostPort[(atIndex + 1)..];

        // Handle IPv6 [::1]
        if (hostPort.StartsWith('['))
        {
            int closeBracket = hostPort.IndexOf(']');
            if (closeBracket < 0)
                return null;

            string host = hostPort[1..closeBracket];
            if (host != "::1")
                return null;

            string portPart = hostPort[(closeBracket + 1)..];
            if (!portPart.StartsWith(':'))
                return null;

            string portStr = portPart[1..];
            if (int.TryParse(portStr, out int port) && port > 0)
                return port;

            return null;
        }

        // Split host:port
        int lastColon = hostPort.LastIndexOf(':');
        if (lastColon < 0)
            return null;

        string hostName = hostPort[..lastColon];
        string portString = hostPort[(lastColon + 1)..];

        if (!hostName.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            hostName != "127.0.0.1")
            return null;

        if (int.TryParse(portString, out int parsedPort) && parsedPort > 0)
            return parsedPort;

        return null;
    }
}
