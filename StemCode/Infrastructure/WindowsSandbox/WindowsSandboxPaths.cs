using StemCode.Infrastructure.Configuration;

namespace StemCode.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxPaths
{
    public const int SetupVersion = 6;
    public const string SandboxGroupName = "StemCodeSandboxUsers";
    public const string LegacySandboxGroupName = "CodexSandboxUsers";
    public const string OfflineUsername = "StemCodeSboxOffline";
    public const string OnlineUsername = "StemCodeSboxOnline";

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

    public static string SandboxDir(string stemCodeHome) => Path.Combine(stemCodeHome, ".sandbox");

    public static string SandboxBinDir(string stemCodeHome) => Path.Combine(stemCodeHome, ".sandbox-bin");

    public static string SandboxSecretsDir(string stemCodeHome) => Path.Combine(stemCodeHome, ".sandbox-secrets");

    public static string SandboxRuntimeDir(string stemCodeHome) => Path.Combine(SandboxDir(stemCodeHome), "runtime");

    public static string SandboxRuntimeProfileDir(string stemCodeHome) => Path.Combine(SandboxRuntimeDir(stemCodeHome), "profile");

    public static string SandboxRuntimeTempDir(string stemCodeHome) => Path.Combine(SandboxRuntimeDir(stemCodeHome), "temp");

    public static string CapSidFile(string stemCodeHome) => Path.Combine(stemCodeHome, "cap_sid");

    public static string SetupMarkerPath(string stemCodeHome) => Path.Combine(SandboxDir(stemCodeHome), "setup_marker.json");

    public static string SandboxUsersPath(string stemCodeHome) => Path.Combine(SandboxSecretsDir(stemCodeHome), "sandbox_users.json");

    public static string SetupErrorPath(string stemCodeHome) => Path.Combine(SandboxDir(stemCodeHome), "setup_error.json");

    public static string SandboxLogPath(string stemCodeHome) => Path.Combine(SandboxDir(stemCodeHome), "sandbox.log");

    public static string[] SandboxGroupNames() =>
    [
        SandboxGroupName,
        LegacySandboxGroupName
    ];

    public static void EnsureStateDirectories(string stemCodeHome)
    {
        Directory.CreateDirectory(stemCodeHome);
        Directory.CreateDirectory(SandboxDir(stemCodeHome));
        Directory.CreateDirectory(SandboxBinDir(stemCodeHome));
        Directory.CreateDirectory(SandboxSecretsDir(stemCodeHome));
        Directory.CreateDirectory(SandboxRuntimeProfileDir(stemCodeHome));
        Directory.CreateDirectory(SandboxRuntimeTempDir(stemCodeHome));
    }
}
