namespace StemCode.Infrastructure.Workspaces;

/// <summary>
/// Host paths shared by the OS sandbox backends so Linux, macOS, and Windows agree on which
/// directories are exposed to sandboxed commands and which are always withheld.
/// </summary>
internal static class SandboxHostPaths
{
    /// <summary>
    /// Home-relative entries that are never exposed to a sandboxed command, even when a
    /// toolchain allow-list would otherwise include them. These hold credentials, private keys,
    /// and cloud session tokens.
    /// </summary>
    internal static readonly string[] SensitiveHomeEntries =
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
        ".pki",
        ".terraform.d",
        ".npmrc",
        ".git-credentials",
    ];

    /// <summary>
    /// Read-only system directories required for ordinary shell commands to resolve
    /// interpreters, libraries, and certificate stores on Linux.
    /// </summary>
    internal static readonly string[] LinuxSystemReadRoots =
    [
        "/bin",
        "/sbin",
        "/lib",
        "/lib32",
        "/lib64",
        "/libx32",
        "/usr",
        "/etc",
        "/opt",
        "/snap",
        // systemd-resolved installs /etc/resolv.conf as a symlink into /run, so DNS
        // resolution needs the stub target as well.
        "/run/systemd/resolve",
    ];

    /// <summary>
    /// Home-relative toolchain caches, SDK roots, and shell startup files that are exposed
    /// read-only. Anything not listed here stays outside the sandbox view of the home
    /// directory.
    /// </summary>
    internal static readonly string[] HomeToolchainEntries =
    [
        ".cache",
        ".local",
        ".asdf",
        ".bun",
        ".cabal",
        ".cargo",
        ".composer",
        ".conda",
        ".deno",
        ".dotnet",
        ".gem",
        ".ghcup",
        ".go",
        "go",
        ".gradle",
        ".m2",
        ".mise",
        ".nuget",
        ".nvm",
        ".pnpm-store",
        ".pub-cache",
        ".pyenv",
        ".rbenv",
        ".rustup",
        ".rvm",
        ".sdkman",
        ".stack",
        ".swiftpm",
        ".volta",
        ".yarn",
        // Login shells (`bash -lc`) need these to build PATH for the toolchains above.
        ".bashrc",
        ".bash_profile",
        ".bash_aliases",
        ".profile",
        ".inputrc",
        ".npm"
    ];

    /// <summary>Resolves the current user home directory, or an empty string when unavailable.</summary>
    internal static string ResolveHomeDirectory()
    {
        string? home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return string.IsNullOrWhiteSpace(home) ? string.Empty : home;
    }

    /// <summary>Whether <paramref name="entryName"/> is a sensitive home entry.</summary>
    internal static bool IsSensitiveHomeEntry(string entryName)
    {
        return SensitiveHomeEntries.Any(sensitive =>
            entryName.Equals(sensitive, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Home-relative toolchain paths that should be exposed read-only, with sensitive entries
    /// removed. Paths are returned even when missing so callers can emit tolerant mounts.
    /// </summary>
    internal static IReadOnlyList<string> HomeReadPaths()
    {
        string home = ResolveHomeDirectory();
        if (string.IsNullOrWhiteSpace(home))
        {
            return [];
        }

        return [.. HomeToolchainEntries
            .Where(static entry => !IsSensitiveHomeEntry(entry))
            .Select(entry => Path.Combine(home, entry))];
    }
}
