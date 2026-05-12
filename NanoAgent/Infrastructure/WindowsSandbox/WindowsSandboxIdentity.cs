using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal sealed record WindowsSandboxCredentials(
    string Username,
    string Password);

internal static class WindowsSandboxIdentity
{
    public static WindowsSandboxCredentials LoadOfflineCredentials(string nanoAgentHome)
    {
        string markerPath = WindowsSandboxPaths.SetupMarkerPath(nanoAgentHome);
        string usersPath = WindowsSandboxPaths.SandboxUsersPath(nanoAgentHome);
        if (!File.Exists(markerPath) || !File.Exists(usersPath))
        {
            throw new InvalidOperationException("Windows sandbox setup is missing.");
        }

        WindowsSandboxSetupMarker marker = JsonSerializer.Deserialize(
            File.ReadAllText(markerPath),
            WindowsSandboxJsonContext.Default.WindowsSandboxSetupMarker) ?? throw new InvalidOperationException("Windows sandbox setup marker is invalid.");
        WindowsSandboxUsersFile users = JsonSerializer.Deserialize(
            File.ReadAllText(usersPath),
            WindowsSandboxJsonContext.Default.WindowsSandboxUsersFile) ?? throw new InvalidOperationException("Windows sandbox users file is invalid.");

        if (!marker.VersionMatches || !users.VersionMatches)
        {
            throw new InvalidOperationException("Windows sandbox setup version is stale.");
        }

        byte[] protectedBytes = Convert.FromBase64String(users.Offline.Password);
        byte[] passwordBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return new WindowsSandboxCredentials(users.Offline.Username, Encoding.UTF8.GetString(passwordBytes));
    }

    public static string ProtectPassword(string password)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(password);
        byte[] protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(bytes);
        return Convert.ToBase64String(protectedBytes);
    }
}
