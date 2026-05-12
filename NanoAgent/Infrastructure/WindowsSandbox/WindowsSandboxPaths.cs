using NanoAgent.Infrastructure.Configuration;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxPaths
{
    public const int SetupVersion = 6;
    public const string SandboxGroupName = "NanoAgentSandboxUsers";
    public const string LegacySandboxGroupName = "CodexSandboxUsers";
    public const string OfflineUsername = "NanoAgentSboxOffline";
    public const string OnlineUsername = "NanoAgentSboxOnline";

    public static string ResolveAppHome()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        return Path.Combine(appData, ApplicationIdentity.StorageDirectoryName);
    }

    public static string SandboxDir(string nanoAgentHome) => Path.Combine(nanoAgentHome, ".sandbox");

    public static string SandboxBinDir(string nanoAgentHome) => Path.Combine(nanoAgentHome, ".sandbox-bin");

    public static string SandboxSecretsDir(string nanoAgentHome) => Path.Combine(nanoAgentHome, ".sandbox-secrets");

    public static string SandboxRuntimeDir(string nanoAgentHome) => Path.Combine(SandboxDir(nanoAgentHome), "runtime");

    public static string SandboxRuntimeProfileDir(string nanoAgentHome) => Path.Combine(SandboxRuntimeDir(nanoAgentHome), "profile");

    public static string SandboxRuntimeTempDir(string nanoAgentHome) => Path.Combine(SandboxRuntimeDir(nanoAgentHome), "temp");

    public static string CapSidFile(string nanoAgentHome) => Path.Combine(nanoAgentHome, "cap_sid");

    public static string SetupMarkerPath(string nanoAgentHome) => Path.Combine(SandboxDir(nanoAgentHome), "setup_marker.json");

    public static string SandboxUsersPath(string nanoAgentHome) => Path.Combine(SandboxSecretsDir(nanoAgentHome), "sandbox_users.json");

    public static string SetupErrorPath(string nanoAgentHome) => Path.Combine(SandboxDir(nanoAgentHome), "setup_error.json");

    public static string SandboxLogPath(string nanoAgentHome) => Path.Combine(SandboxDir(nanoAgentHome), "sandbox.log");

    public static string[] SandboxGroupNames() =>
    [
        SandboxGroupName,
        LegacySandboxGroupName
    ];

    public static void EnsureStateDirectories(string nanoAgentHome)
    {
        Directory.CreateDirectory(nanoAgentHome);
        Directory.CreateDirectory(SandboxDir(nanoAgentHome));
        Directory.CreateDirectory(SandboxBinDir(nanoAgentHome));
        Directory.CreateDirectory(SandboxSecretsDir(nanoAgentHome));
        Directory.CreateDirectory(SandboxRuntimeProfileDir(nanoAgentHome));
        Directory.CreateDirectory(SandboxRuntimeTempDir(nanoAgentHome));
    }
}
