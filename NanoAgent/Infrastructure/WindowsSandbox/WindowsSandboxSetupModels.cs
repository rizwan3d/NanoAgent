using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal sealed class WindowsSandboxSetupMarker
{
    public int Version { get; set; }

    public string OfflineUsername { get; set; } = string.Empty;

    public string OnlineUsername { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Proxy ports used when the sandbox runs in offline network identity.</summary>
    public List<int> ProxyPorts { get; set; } = [];

    /// <summary>Whether local network binding is allowed in offline identity.</summary>
    public bool AllowLocalBinding { get; set; }

    public bool VersionMatches => Version == WindowsSandboxPaths.SetupVersion;

    /// <summary>
    /// Check if the stored marker's offline firewall settings mismatch the
    /// desired settings, which would require a setup refresh.
    /// </summary>
    internal string? RequestMismatchReason(
        WindowsSandboxSetupRoots.SandboxNetworkIdentity networkIdentity,
        WindowsSandboxSetupRoots.OfflineProxySettings desiredSettings)
    {
        if (networkIdentity == WindowsSandboxSetupRoots.SandboxNetworkIdentity.Online)
        {
            return null;
        }

        if (ProxyPorts.Count == desiredSettings.ProxyPorts.Count &&
            ProxyPorts.SequenceEqual(desiredSettings.ProxyPorts) &&
            AllowLocalBinding == desiredSettings.AllowLocalBinding)
        {
            return null;
        }

        return $"offline firewall settings changed (stored_ports=[{string.Join(",", ProxyPorts)}], " +
               $"desired_ports=[{string.Join(",", desiredSettings.ProxyPorts)}], " +
               $"stored_allow_local_binding={AllowLocalBinding}, " +
               $"desired_allow_local_binding={desiredSettings.AllowLocalBinding})";
    }
}

internal sealed class WindowsSandboxUserRecord
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

internal sealed class WindowsSandboxUsersFile
{
    public int Version { get; set; }

    public WindowsSandboxUserRecord Offline { get; set; } = new();

    public WindowsSandboxUserRecord Online { get; set; } = new();

    public bool VersionMatches => Version == WindowsSandboxPaths.SetupVersion;
}

internal sealed class WindowsSandboxSetupPayload
{
    public int Version { get; set; }

    public string NanoAgentHome { get; set; } = string.Empty;

    public string CommandCwd { get; set; } = string.Empty;

    public string[] ReadRoots { get; set; } = [];

    public string[] WriteRoots { get; set; } = [];

    public string[] DenyWritePaths { get; set; } = [];

    public string RealUser { get; set; } = string.Empty;

    public string[] SandboxUsernames { get; set; } = [];

    public bool RefreshOnly { get; set; }

    /// <summary>Proxy ports for offline firewall rules.</summary>
    public List<int> ProxyPorts { get; set; } = [];

    /// <summary>Whether to allow local network binding in offline identity.</summary>
    public bool AllowLocalBinding { get; set; }
}

internal sealed class WindowsSandboxSetupError
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int? Win32Error { get; set; }
}

internal sealed class WindowsSandboxRunnerPayload
{
    public string NanoAgentHome { get; set; } = string.Empty;

    public string CommandCwd { get; set; } = string.Empty;

    public ToolSandboxModeDto Mode { get; set; }

    public bool UsePrivateDesktop { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string[] Arguments { get; set; } = [];

    public string? StandardInput { get; set; }

    public string? WorkingDirectory { get; set; }

    public int? MaxOutputCharacters { get; set; }

    public Dictionary<string, string>? EnvironmentVariables { get; set; }
}

internal enum ToolSandboxModeDto
{
    ReadOnly,
    WorkspaceWrite
}

internal sealed class WindowsSandboxRunnerResult
{
    public int ExitCode { get; set; }

    public string Stdout { get; set; } = string.Empty;

    public string Stderr { get; set; } = string.Empty;

    public string? Error { get; set; }
}
