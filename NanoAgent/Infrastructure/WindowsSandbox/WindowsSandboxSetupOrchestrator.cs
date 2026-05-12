using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using NanoAgent.Application.Models;
using System.Text.Json;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxSetupOrchestrator
{
    public const string SetupCommandArgument = "--nanoagent-windows-sandbox-setup";
    private const string CliAssemblyFileName = "NanoAgent.CLI.dll";
    private const string CliExecutableFileName = "NanoAgent.CLI.exe";

    public static void EnsureSetupFresh(WindowsSandboxSetupPayload payload, bool needsElevation)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows sandbox setup is only available on Windows.");
        }

        payload.Version = WindowsSandboxPaths.SetupVersion;
        WindowsSandboxPaths.EnsureStateDirectories(payload.NanoAgentHome);
        string markerPath = WindowsSandboxPaths.SetupMarkerPath(payload.NanoAgentHome);
        if (File.Exists(markerPath) && File.Exists(WindowsSandboxPaths.SandboxUsersPath(payload.NanoAgentHome)))
        {
            WindowsSandboxSetupMarker? marker = JsonSerializer.Deserialize(
                File.ReadAllText(markerPath),
                WindowsSandboxJsonContext.Default.WindowsSandboxSetupMarker);
            WindowsSandboxUsersFile? users = JsonSerializer.Deserialize(
                File.ReadAllText(WindowsSandboxPaths.SandboxUsersPath(payload.NanoAgentHome)),
                WindowsSandboxJsonContext.Default.WindowsSandboxUsersFile);
            if (marker?.VersionMatches == true && users?.VersionMatches == true)
            {
                // Check proxy/firewall mismatch — if offline settings changed, force refresh
                var networkIdentity = WindowsSandboxSetupRoots.NetworkIdentityFromPolicy(
                    ToolSandboxMode.ReadOnly, proxyEnforced: false);
                var desiredSettings = new WindowsSandboxSetupRoots.OfflineProxySettings(
                    payload.ProxyPorts ?? [],
                    payload.AllowLocalBinding);
                string? mismatch = marker.RequestMismatchReason(networkIdentity, desiredSettings);
                if (mismatch is null)
                {
                    return;
                }

                // Proxy settings changed — fall through to setup refresh
            }
        }

        RunSetup(payload, needsElevation);
    }

    public static bool IsElevated()
    {
        WindowsSandboxNative.SidIdentifierAuthority ntAuthority = new()
        {
            Value = [0, 0, 0, 0, 0, 5]
        };

        if (!WindowsSandboxNative.AllocateAndInitializeSid(
                ref ntAuthority,
                2,
                WindowsSandboxNative.SecurityBuiltinDomainRid,
                WindowsSandboxNative.DomainAliasRidAdmins,
                0,
                0,
                0,
                0,
                0,
                0,
                out IntPtr adminSid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AllocateAndInitializeSid failed.");
        }

        try
        {
            if (!WindowsSandboxNative.CheckTokenMembership(IntPtr.Zero, adminSid, out bool isMember))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CheckTokenMembership failed.");
            }

            return isMember;
        }
        finally
        {
            WindowsSandboxNative.FreeSid(adminSid);
        }
    }

    public static int RunEncodedSetupPayload(string payloadB64)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows sandbox setup is only available on Windows.");
        }

        WindowsSandboxSetupPayload payload = DecodePayload(payloadB64);
        try
        {
            ExecuteSetup(payload);
            ClearSetupErrorReport(payload.NanoAgentHome);
            return 0;
        }
        catch (Exception exception)
        {
            WriteSetupErrorReport(payload.NanoAgentHome, exception);
            return 1;
        }
    }

    public static void RunSetup(WindowsSandboxSetupPayload payload, bool needsElevation)
    {
        string payloadB64 = EncodePayload(payload);
        ClearSetupErrorReport(payload.NanoAgentHome);

        if (!needsElevation || IsElevated())
        {
            ExecuteSetup(payload);
            ClearSetupErrorReport(payload.NanoAgentHome);
            return;
        }

        (string setupExe, string setupParametersPrefix) = CreateSelfSetupLaunchCommand();
        WindowsSandboxNative.ShellExecuteInfo shellExecute = new()
        {
            cbSize = Marshal.SizeOf<WindowsSandboxNative.ShellExecuteInfo>(),
            fMask = WindowsSandboxNative.SeeMaskNoCloseProcess,
            lpVerb = "runas",
            lpFile = setupExe,
            lpParameters = setupParametersPrefix + QuoteArg(SetupCommandArgument) + " " + QuoteArg(payloadB64),
            nShow = WindowsSandboxNative.SwHide
        };

        if (!WindowsSandboxNative.ShellExecuteEx(ref shellExecute))
        {
            int error = Marshal.GetLastWin32Error();
            if ((uint)error == WindowsSandboxNative.ErrorCancelled)
            {
                throw new OperationCanceledException("Windows sandbox setup elevation was canceled.");
            }

            throw new Win32Exception(error, "ShellExecuteExW failed for Windows sandbox setup helper.");
        }

        try
        {
            WindowsSandboxNative.WaitForSingleObject(shellExecute.hProcess, WindowsSandboxNative.Infinite);
            if (!WindowsSandboxNative.GetExitCodeProcess(shellExecute.hProcess, out uint exitCode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeProcess failed for Windows sandbox setup helper.");
            }

            if (exitCode != 0)
            {
                throw ReadSetupErrorOrExitCode(payload.NanoAgentHome, unchecked((int)exitCode));
            }

            ClearSetupErrorReport(payload.NanoAgentHome);
        }
        finally
        {
            if (shellExecute.hProcess != IntPtr.Zero)
            {
                WindowsSandboxNative.CloseHandle(shellExecute.hProcess);
            }
        }
    }

    private static (string FileName, string ParametersPrefix) CreateSelfSetupLaunchCommand()
    {
        string processPath = Environment.ProcessPath ??
            throw new InvalidOperationException("Unable to locate the current NanoAgent executable for elevated Windows sandbox setup.");
        string sourceDirectory = AppContext.BaseDirectory;
        string cliExecutablePath = Path.Combine(sourceDirectory, CliExecutableFileName);
        if (File.Exists(cliExecutablePath))
        {
            return (cliExecutablePath, string.Empty);
        }

        string processName = Path.GetFileNameWithoutExtension(processPath);
        if (!string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return (processPath, string.Empty);
        }

        string entryAssemblyPath = Path.Combine(sourceDirectory, CliAssemblyFileName);
        if (!File.Exists(entryAssemblyPath))
        {
            string entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name ??
                throw new InvalidOperationException("Unable to locate the current NanoAgent assembly for elevated Windows sandbox setup.");
            entryAssemblyPath = Path.Combine(sourceDirectory, entryAssemblyName + ".dll");
        }

        if (!File.Exists(entryAssemblyPath))
        {
            throw new InvalidOperationException("Unable to locate the current NanoAgent assembly for elevated Windows sandbox setup.");
        }

        return (processPath, QuoteArg(entryAssemblyPath) + " ");
    }

    private static void ExecuteSetup(WindowsSandboxSetupPayload payload)
    {
        if (!payload.RefreshOnly)
        {
            WindowsSandboxUserProvisioner.EnsureUsers(payload.NanoAgentHome);
        }
        else
        {
            WindowsSandboxPaths.EnsureStateDirectories(payload.NanoAgentHome);
        }

        WindowsSandboxHelperMaterializer.RefreshHelperArtifacts(payload.NanoAgentHome);

        WriteSetupMarker(payload);
    }

    private static void WriteSetupMarker(WindowsSandboxSetupPayload payload)
    {
        string nanoAgentHome = payload.NanoAgentHome;
        WindowsSandboxPaths.EnsureStateDirectories(nanoAgentHome);
        File.WriteAllText(
            WindowsSandboxPaths.SetupMarkerPath(nanoAgentHome),
            JsonSerializer.Serialize(
                new WindowsSandboxSetupMarker
                {
                    Version = WindowsSandboxPaths.SetupVersion,
                    OfflineUsername = WindowsSandboxPaths.OfflineUsername,
                    OnlineUsername = WindowsSandboxPaths.OnlineUsername,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ProxyPorts = payload.ProxyPorts ?? [],
                    AllowLocalBinding = payload.AllowLocalBinding
                },
                WindowsSandboxJsonContext.Default.WindowsSandboxSetupMarker));
    }

    private static string EncodePayload(WindowsSandboxSetupPayload payload)
    {
        string json = JsonSerializer.Serialize(payload, WindowsSandboxJsonContext.Default.WindowsSandboxSetupPayload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static WindowsSandboxSetupPayload DecodePayload(string payloadB64)
    {
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(payloadB64));
        return JsonSerializer.Deserialize(json, WindowsSandboxJsonContext.Default.WindowsSandboxSetupPayload) ??
            throw new InvalidOperationException("Windows sandbox setup payload is invalid.");
    }

    private static void ClearSetupErrorReport(string nanoAgentHome)
    {
        string path = WindowsSandboxPaths.SetupErrorPath(nanoAgentHome);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static Exception ReadSetupErrorOrExitCode(string nanoAgentHome, int exitCode)
    {
        string path = WindowsSandboxPaths.SetupErrorPath(nanoAgentHome);
        if (File.Exists(path))
        {
            WindowsSandboxSetupError? error = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                WindowsSandboxJsonContext.Default.WindowsSandboxSetupError);
            if (error is not null)
            {
                return new InvalidOperationException(
                    $"Windows sandbox setup failed: {error.Code}: {error.Message}" +
                    (error.Win32Error is null ? string.Empty : $" (Win32 {error.Win32Error})"));
            }
        }

        return new InvalidOperationException($"Windows sandbox setup helper exited with code {exitCode}.");
    }

    private static void WriteSetupErrorReport(string nanoAgentHome, Exception exception)
    {
        WindowsSandboxPaths.EnsureStateDirectories(nanoAgentHome);
        File.WriteAllText(
            WindowsSandboxPaths.SetupErrorPath(nanoAgentHome),
            JsonSerializer.Serialize(
                new WindowsSandboxSetupError
                {
                    Code = exception.GetType().Name,
                    Message = exception.Message,
                    Win32Error = exception is Win32Exception win32Exception
                        ? win32Exception.NativeErrorCode
                        : null
                },
                WindowsSandboxJsonContext.Default.WindowsSandboxSetupError));
    }

    private static string QuoteArg(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
