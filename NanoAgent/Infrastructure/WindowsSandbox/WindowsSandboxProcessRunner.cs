using Microsoft.Win32.SafeHandles;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Secrets;
using System.Collections;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxProcessRunner
{
    public const string RunnerCommandArgument = "--nanoagent-windows-sandbox-runner";
    private const uint HandleFlagInherit = 0x00000001;
    private const int BufferSize = 4096;
    private const int CreateProcessWithLogonWCommandLineLimit = 1024;
    internal const int StatusDllInitFailed = unchecked((int)0xC0000142);

    public static async Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows sandbox process launch is only available on Windows.");
        }

        WindowsSandboxPaths.EnsureStateDirectories(context.NanoAgentHome);
        WindowsSandboxLog.Write(context.NanoAgentHome, $"command start: mode={context.Mode}, cwd={context.CommandCwd}, file={request.FileName}, private_desktop={context.UsePrivateDesktop}");
        ProcessExecutionResult result = await RunWithPreparedAclsAsync(
            request,
            context,
            cancellationToken,
            static (innerRequest, innerContext, innerCancellationToken) => LaunchViaSelfRunnerAsync(
                innerRequest,
                innerContext,
                innerCancellationToken));
        WindowsSandboxLog.Write(context.NanoAgentHome, $"command exit: code={result.ExitCode}");
        return result;
    }

    internal static Task<ProcessExecutionResult> RunPreparedRestrictedChildAsync(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken)
    {
        return RunWithPreparedAclsAsync(
            request,
            context,
            cancellationToken,
            static (innerRequest, innerContext, innerCancellationToken) => RunRestrictedChildAsync(
                innerRequest,
                innerContext,
                innerCancellationToken));
    }

    private static async Task<ProcessExecutionResult> RunWithPreparedAclsAsync(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken,
        Func<ProcessExecutionRequest, WindowsSandboxExecutionContext, CancellationToken, Task<ProcessExecutionResult>> launcher)
    {
        WindowsAllowDenyPaths paths = WindowsAllowDenyPlanner.Compute(
            context.Mode,
            context.PolicyCwd,
            context.CommandCwd,
            context.WritableRoots,
            request.EnvironmentVariables,
            context.IncludeTempEnvironmentVariables);
        EnsureSetupFresh(context, paths);

        WindowsCapabilitySids capabilitySids = WindowsCapabilitySidStore.LoadOrCreate(context.NanoAgentHome);
        string workspaceSid = WindowsCapabilitySidStore.WorkspaceSidForCwd(context.NanoAgentHome, context.CommandCwd);
        request = MaterializeExternalExecutableIfNeeded(request, context);

        List<(string Path, string Sid, FileSystemRights Rights, AccessControlType Type)> temporaryAces = [];
        try
        {
            string sandboxGroupSid = SandboxGroupSids()[0];
            PrepareAcls(request, context, capabilitySids, workspaceSid, sandboxGroupSid, paths, temporaryAces);
            ProcessExecutionResult result = await ExecuteWithStartupRetryAsync(
                async innerCancellationToken => await launcher(
                    request,
                    context,
                    innerCancellationToken),
                context.NanoAgentHome,
                cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            WindowsSandboxLog.Write(context.NanoAgentHome, $"command failure: {exception.GetType().Name}: {exception.Message}");
            throw;
        }
        finally
        {
            foreach ((string path, string sid, FileSystemRights rights, AccessControlType type) in temporaryAces)
            {
                try
                {
                    WindowsSandboxAcl.RevokeAce(path, sid, rights, type);
                }
                catch (Exception exception)
                {
                    WindowsSandboxLog.Write(context.NanoAgentHome, $"cleanup failure: revoke ACE {path}: {exception.Message}");
                }
            }
        }
    }

    public static int RunPipeRunner(string pipeIn, string pipeOut)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows sandbox runner is only available on Windows.");
        }

        try
        {
            RunPipeRunnerAsync(pipeIn, pipeOut, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Windows sandbox runner failed: {exception.Message}");
            return 126;
        }
    }

    private static async Task<ProcessExecutionResult> RunRestrictedChildAsync(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken)
    {
        WindowsCapabilitySids capabilitySids = WindowsCapabilitySidStore.LoadOrCreate(context.NanoAgentHome);
        string workspaceSid = WindowsCapabilitySidStore.WorkspaceSidForCwd(context.NanoAgentHome, context.CommandCwd);
        string[] restrictingSids = context.Mode == ToolSandboxMode.ReadOnly
            ? [capabilitySids.Readonly]
            : [capabilitySids.Workspace, workspaceSid];
        bool includeCurrentUserSid = string.Equals(Environment.UserName, WindowsSandboxPaths.OfflineUsername, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(Environment.UserName, WindowsSandboxPaths.OnlineUsername, StringComparison.OrdinalIgnoreCase);

        using WindowsSandboxToken token = includeCurrentUserSid
            ? WindowsSandboxToken.CreateRestrictedForCurrentUser(restrictingSids)
            : WindowsSandboxToken.CreateRestricted(restrictingSids);
        return await LaunchWithCreateProcessAsUserAsync(request, token, context, cancellationToken);
    }

    private static async Task<ProcessExecutionResult> LaunchViaSelfRunnerAsync(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken)
    {
        WindowsSandboxRunnerPayload payload = new()
        {
            NanoAgentHome = context.NanoAgentHome,
            CommandCwd = context.CommandCwd,
            Mode = context.Mode == ToolSandboxMode.WorkspaceWrite
                ? ToolSandboxModeDto.WorkspaceWrite
                : ToolSandboxModeDto.ReadOnly,
            UsePrivateDesktop = context.UsePrivateDesktop,
            FileName = request.FileName,
            Arguments = [.. request.Arguments],
            StandardInput = request.StandardInput,
            WorkingDirectory = request.WorkingDirectory ?? context.CommandCwd,
            MaxOutputCharacters = request.MaxOutputCharacters,
            EnvironmentVariables = request.EnvironmentVariables is null
                ? null
                : new Dictionary<string, string>(request.EnvironmentVariables, StringComparer.OrdinalIgnoreCase)
        };

        string payloadB64 = EncodeRunnerPayload(payload);
        WindowsSandboxLaunchCommand launchCommand = CreateSelfLaunchCommand(context.NanoAgentHome);
        (string pipeIn, string pipeOut) = WindowsSandboxRunnerClient.CreatePipeNames();
        string parameters = CreateRunnerBootstrapParameters(
            launchCommand.ParametersPrefix,
            pipeIn,
            pipeOut);
        string commandLineText = QuoteWindowsArgument(launchCommand.FileName) + " " + parameters;
        const string domain = ".";
        const uint creationFlags = WindowsSandboxNative.CreateUnicodeEnvironment | WindowsSandboxNative.CreateNoWindow;
        const bool environmentBlockIsNull = true;

        WindowsSandboxNative.ProcessInformation processInformation = default;
        string sandboxUserSid = string.Empty;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            WindowsSandboxCredentials credentials;
            try
            {
                credentials = WindowsSandboxIdentity.LoadOfflineCredentials(context.NanoAgentHome);
            }
            catch (Exception exception)
            {
                WindowsSandboxRunnerStartupContext credentialFailureContext = new(
                    context.NanoAgentHome,
                    Path.GetFullPath(launchCommand.FileName),
                    commandLineText,
                    context.CommandCwd,
                    WindowsSandboxPaths.OfflineUsername,
                    domain,
                    creationFlags,
                    LogonWithProfile: true,
                    environmentBlockIsNull);
                WindowsSandboxRunnerStartupDiagnostics credentialDiagnostics =
                    WindowsSandboxRunnerStartupDiagnostics.FromWin32Error(1326, credentialFailureContext);
                credentialDiagnostics.Log(context.NanoAgentHome);
                WindowsSandboxLog.Write(context.NanoAgentHome, $"runner credential load failure: {exception.GetType().Name}: {exception.Message}");
                if (attempt == 0 && TryRecoverRunnerStartup(context, 1326))
                {
                    WindowsSandboxLog.Write(context.NanoAgentHome, "runner credential recovery attempted");
                    continue;
                }

                throw new Win32Exception(1326, credentialDiagnostics.Text + Environment.NewLine + "credential_load_error=" + exception.Message);
            }

            WindowsSandboxRunnerStartupContext startupContext = new(
                context.NanoAgentHome,
                Path.GetFullPath(launchCommand.FileName),
                commandLineText,
                context.CommandCwd,
                credentials.Username,
                domain,
                creationFlags,
                LogonWithProfile: true,
                environmentBlockIsNull);
            sandboxUserSid = ResolveAccountSid(credentials.Username, domain).Value;

            try
            {
                EnsureRunnerStartupPreflight(context, startupContext);
            }
            catch (Win32Exception exception) when (attempt == 0 && TryRecoverRunnerStartup(context, exception.NativeErrorCode))
            {
                WindowsSandboxLog.Write(context.NanoAgentHome, $"runner startup preflight recovery attempted: error={exception.NativeErrorCode}");
                continue;
            }
            StringBuilder commandLine = new(commandLineText);
            WindowsSandboxNative.StartupInfo startupInfo = new()
            {
                cb = Marshal.SizeOf<WindowsSandboxNative.StartupInfo>()
            };

            bool created = WindowsSandboxNative.CreateProcessWithLogonW(
                credentials.Username,
                domain,
                credentials.Password,
                WindowsSandboxNative.LogonWithProfile,
                launchCommand.FileName,
                commandLine,
                creationFlags,
                IntPtr.Zero,
                context.CommandCwd,
                ref startupInfo,
                out processInformation);
            if (created)
            {
                break;
            }

            int error = Marshal.GetLastWin32Error();
            WindowsSandboxRunnerStartupDiagnostics diagnostics =
                WindowsSandboxRunnerStartupDiagnostics.FromWin32Error(
                    error,
                    startupContext,
                    commandLineTooLong: IsCommandLineTooLongForCreateProcessWithLogonW(error, commandLineText));
            diagnostics.Log(context.NanoAgentHome);
            if (attempt == 0 && TryRecoverRunnerStartup(context, error))
            {
                WindowsSandboxLog.Write(context.NanoAgentHome, $"runner startup recovery attempted: error={error}");
                continue;
            }

            throw new Win32Exception(error, diagnostics.Text);
        }

        try
        {
            using NamedPipeServerStream inboundServer = WindowsSandboxRunnerClient.CreateInboundServer(pipeIn, sandboxUserSid);
            using NamedPipeServerStream outboundServer = WindowsSandboxRunnerClient.CreateOutboundServer(pipeOut, sandboxUserSid);
            WindowsSandboxLog.Write(context.NanoAgentHome, $"runner launched: pid={processInformation.dwProcessId}, file={launchCommand.FileName}");

            await Task.WhenAll(
                inboundServer.WaitForConnectionAsync(cancellationToken),
                outboundServer.WaitForConnectionAsync(cancellationToken));

            await WindowsSandboxFramedIpc.WriteAsync(
                inboundServer,
                new WindowsSandboxIpcMessage
                {
                    Kind = WindowsSandboxIpcMessageKind.SpawnRequest,
                    PayloadBase64 = payloadB64
                },
                cancellationToken);

            WindowsSandboxIpcMessage ready = await WindowsSandboxFramedIpc.ReadAsync(outboundServer, cancellationToken);
            if (ready.Kind != WindowsSandboxIpcMessageKind.SpawnReady)
            {
                throw new InvalidOperationException("Windows sandbox runner did not acknowledge the spawn request.");
            }

            while (true)
            {
                WindowsSandboxIpcMessage message = await WindowsSandboxFramedIpc.ReadAsync(outboundServer, cancellationToken);
                switch (message.Kind)
                {
                    case WindowsSandboxIpcMessageKind.Exit:
                        if (string.IsNullOrWhiteSpace(message.PayloadBase64))
                        {
                            throw new InvalidOperationException("Windows sandbox runner exit message was missing the result payload.");
                        }

                        WindowsSandboxRunnerResult result = DecodeRunnerResult(message.PayloadBase64);
                        return new ProcessExecutionResult(result.ExitCode, result.Stdout, result.Stderr);

                    case WindowsSandboxIpcMessageKind.Error:
                        throw new InvalidOperationException(message.Error ?? "Windows sandbox runner reported an unknown startup failure.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (processInformation.hProcess != IntPtr.Zero &&
                WindowsSandboxNative.GetExitCodeProcess(processInformation.hProcess, out uint exitCode))
            {
                WindowsSandboxLog.Write(context.NanoAgentHome, $"runner connect canceled: pid={processInformation.dwProcessId}, exit_code={unchecked((int)exitCode)}");
            }

            if (processInformation.hProcess != IntPtr.Zero)
            {
                WindowsSandboxNative.TerminateProcess(processInformation.hProcess, 1);
            }

            throw;
        }
        finally
        {
            if (processInformation.hThread != IntPtr.Zero)
            {
                WindowsSandboxNative.CloseHandle(processInformation.hThread);
            }

            if (processInformation.hProcess != IntPtr.Zero)
            {
                WindowsSandboxNative.CloseHandle(processInformation.hProcess);
            }
        }
    }

    private static void EnsureSetupFresh(
        WindowsSandboxExecutionContext context,
        WindowsAllowDenyPaths paths)
    {
        WindowsSandboxSetupOrchestrator.EnsureSetupFresh(
            new WindowsSandboxSetupPayload
            {
                NanoAgentHome = context.NanoAgentHome,
                CommandCwd = context.CommandCwd,
                ReadRoots = [context.CommandCwd, .. WindowsSandboxSetupRoots.PlatformDefaultReadRoots],
                WriteRoots = [.. paths.Allow],
                DenyWritePaths = [.. paths.Deny],
                RealUser = Environment.UserName,
                SandboxUsernames =
                [
                    WindowsSandboxPaths.OfflineUsername,
                    WindowsSandboxPaths.OnlineUsername
                ]
            },
            needsElevation: !WindowsSandboxSetupOrchestrator.IsElevated());
    }

    private static void EnsureRunnerStartupPreflight(
        WindowsSandboxExecutionContext context,
        WindowsSandboxRunnerStartupContext startupContext)
    {
        if (!File.Exists(WindowsSandboxPaths.SetupMarkerPath(context.NanoAgentHome)) ||
            !File.Exists(WindowsSandboxPaths.SandboxUsersPath(context.NanoAgentHome)))
        {
            ThrowRunnerStartupPreflightFailure(2, startupContext);
        }

        if (!Path.IsPathFullyQualified(startupContext.RunnerExecutablePath) ||
            !File.Exists(startupContext.RunnerExecutablePath))
        {
            ThrowRunnerStartupPreflightFailure(2, startupContext);
        }

        if (string.IsNullOrWhiteSpace(startupContext.Cwd) || !Directory.Exists(startupContext.Cwd))
        {
            ThrowRunnerStartupPreflightFailure(267, startupContext);
        }

        EnsureLocalAccountResolvable(startupContext.Username, startupContext.Domain, startupContext);
        EnsureLocalAccountResolvable(WindowsSandboxPaths.SandboxGroupName, startupContext.Domain, startupContext);
        EnsureSandboxUserGroupMembership(startupContext.Username, startupContext);
        EnsureSandboxBinUsable(context.NanoAgentHome, startupContext);
        EnsureSandboxPipeSecurityDescriptorBuilds(startupContext.Username, startupContext.Domain, startupContext);
    }

    private static void ThrowRunnerStartupPreflightFailure(
        int error,
        WindowsSandboxRunnerStartupContext startupContext)
    {
        WindowsSandboxRunnerStartupDiagnostics diagnostics =
            WindowsSandboxRunnerStartupDiagnostics.FromWin32Error(
                error,
                startupContext,
                commandLineTooLong: IsCommandLineTooLongForCreateProcessWithLogonW(error, startupContext.CommandLine));
        diagnostics.Log(startupContext.NanoAgentHome);
        throw new Win32Exception(error, diagnostics.Text);
    }

    private static void EnsureLocalAccountResolvable(
        string accountName,
        string domain,
        WindowsSandboxRunnerStartupContext startupContext)
    {
        try
        {
            _ = ResolveAccountSid(accountName, domain);
        }
        catch
        {
            ThrowRunnerStartupPreflightFailure(1326, startupContext);
        }
    }

    private static void EnsureSandboxUserGroupMembership(
        string username,
        WindowsSandboxRunnerStartupContext startupContext)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!WindowsSandboxUserProvisioner.IsUserInLocalGroup(username, WindowsSandboxPaths.SandboxGroupName))
        {
            ThrowRunnerStartupPreflightFailure(1385, startupContext);
        }
    }

    private static void EnsureSandboxBinUsable(
        string nanoAgentHome,
        WindowsSandboxRunnerStartupContext startupContext)
    {
        string sandboxBin = WindowsSandboxPaths.SandboxBinDir(nanoAgentHome);
        if (!Directory.Exists(sandboxBin))
        {
            ThrowRunnerStartupPreflightFailure(3, startupContext);
        }

        try
        {
            _ = Directory.EnumerateFileSystemEntries(sandboxBin).Take(1).ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            ThrowRunnerStartupPreflightFailure(5, startupContext);
        }
    }

    private static void EnsureSandboxPipeSecurityDescriptorBuilds(
        string username,
        string domain,
        WindowsSandboxRunnerStartupContext startupContext)
    {
        try
        {
            string sid = ResolveAccountSid(username, domain).Value;
            _ = WindowsSandboxRunnerClient.CreateSandboxUserPipeSecurityDescriptor(sid);
        }
        catch
        {
            ThrowRunnerStartupPreflightFailure(1326, startupContext);
        }
    }

    private static bool TryRecoverRunnerStartup(
        WindowsSandboxExecutionContext context,
        int error)
    {
        WindowsSandboxSetupPayload payload = new()
        {
            Version = WindowsSandboxPaths.SetupVersion,
            NanoAgentHome = context.NanoAgentHome,
            CommandCwd = context.CommandCwd,
            ReadRoots = [context.CommandCwd, .. WindowsSandboxSetupRoots.PlatformDefaultReadRoots],
            WriteRoots = [],
            DenyWritePaths = [],
            RealUser = Environment.UserName,
            SandboxUsernames =
            [
                WindowsSandboxPaths.OfflineUsername,
                WindowsSandboxPaths.OnlineUsername
            ],
            RefreshOnly = ShouldRefreshOnlyForRunnerStartupError(error)
        };

        try
        {
            WindowsSandboxSetupOrchestrator.RunSetup(
                payload,
                needsElevation: !WindowsSandboxSetupOrchestrator.IsElevated());
            return true;
        }
        catch (OperationCanceledException exception)
        {
            WindowsSandboxLog.Write(context.NanoAgentHome, $"runner startup recovery canceled: {exception.Message}");
            throw new InvalidOperationException(
                "Windows sandbox setup elevation was canceled; the command was not run unsandboxed.",
                exception);
        }
        catch (Exception exception)
        {
            WindowsSandboxLog.Write(context.NanoAgentHome, $"runner startup recovery failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static bool ShouldRefreshOnlyForRunnerStartupError(int error)
    {
        return (uint)error switch
        {
            5 or 3 or 267 => true,
            _ => false
        };
    }

    private static SecurityIdentifier ResolveAccountSid(string accountName, string domain)
    {
        NTAccount account = string.IsNullOrWhiteSpace(domain) || domain == "."
            ? new NTAccount(Environment.MachineName, accountName)
            : new NTAccount(domain, accountName);
        return (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
    }

    public static string BuildWindowsCommandLine(ProcessExecutionRequest request)
    {
        return string.Join(
            " ",
            new[] { request.FileName }
                .Concat(request.Arguments)
                .Select(QuoteWindowsArgument));
    }

    private static string EncodeRunnerPayload(WindowsSandboxRunnerPayload payload)
    {
        string json = JsonSerializer.Serialize(payload, WindowsSandboxJsonContext.Default.WindowsSandboxRunnerPayload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static WindowsSandboxRunnerPayload DecodeRunnerPayload(string payloadB64)
    {
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(payloadB64));
        return JsonSerializer.Deserialize(json, WindowsSandboxJsonContext.Default.WindowsSandboxRunnerPayload)
               ?? throw new InvalidOperationException("Windows sandbox runner payload is invalid.");
    }

    private static string EncodeRunnerResult(WindowsSandboxRunnerResult result)
    {
        string json = JsonSerializer.Serialize(result, WindowsSandboxJsonContext.Default.WindowsSandboxRunnerResult);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static WindowsSandboxRunnerResult DecodeRunnerResult(string payloadB64)
    {
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(payloadB64));
        return JsonSerializer.Deserialize(
                   json,
                   WindowsSandboxJsonContext.Default.WindowsSandboxRunnerResult)
               ?? throw new InvalidOperationException("Windows sandbox runner result payload is invalid.");
    }

    internal static WindowsSandboxLaunchCommand CreateSelfLaunchCommand(string nanoAgentHome)
        => WindowsSandboxHelperMaterializer.ResolveSelfLaunchCommand(nanoAgentHome);

    internal static string CreateRunnerBootstrapParameters(
        string parametersPrefix,
        string pipeIn,
        string pipeOut)
    {
        return parametersPrefix +
               QuoteWindowsArgument(RunnerCommandArgument) + " " +
               QuoteWindowsArgument("--pipe-in=" + WindowsSandboxRunnerClient.CreatePipeArgument(pipeIn)) + " " +
               QuoteWindowsArgument("--pipe-out=" + WindowsSandboxRunnerClient.CreatePipeArgument(pipeOut));
    }

    internal static bool IsCommandLineTooLongForCreateProcessWithLogonW(int error, string commandLineText)
    {
        return error == 87 && commandLineText.Length > CreateProcessWithLogonWCommandLineLimit;
    }

    public static byte[] BuildEnvironmentBlockBytes(IReadOnlyDictionary<string, string>? environmentVariables)
    {
        return BuildEnvironmentBlockBytes(environmentVariables, nanoAgentHome: null);
    }

    private static byte[] BuildEnvironmentBlockBytes(
        IReadOnlyDictionary<string, string>? environmentVariables,
        string? nanoAgentHome)
    {
        IReadOnlyDictionary<string, string> normalized = BuildSandboxEnvironmentMap(
            environmentVariables,
            nanoAgentHome);

        string block = string.Join(
            '\0',
            normalized
                .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static item => $"{item.Key}={item.Value}")) + "\0\0";
        return Encoding.Unicode.GetBytes(block);
    }

    private static IReadOnlyDictionary<string, string> BuildSandboxEnvironmentMap(
        IReadOnlyDictionary<string, string>? environmentVariables,
        string? nanoAgentHome)
    {
        Dictionary<string, string> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                merged[key] = value;
            }
        }

        if (environmentVariables is not null)
        {
            foreach (KeyValuePair<string, string> item in environmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(item.Key))
                {
                    merged[item.Key] = item.Value;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(nanoAgentHome))
        {
            ApplySandboxRuntimeEnvironment(merged, nanoAgentHome);
        }

        Dictionary<string, string> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> item in merged)
        {
            normalized[CanonicalizeWindowsEnvironmentKey(item.Key)] = item.Value;
        }

        return normalized;
    }

    private static void ApplySandboxRuntimeEnvironment(
        IDictionary<string, string> environment,
        string nanoAgentHome)
    {
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string systemDir = Environment.SystemDirectory;
        WindowsSandboxRuntimeLayout layout = EnsureSandboxRuntimeLayout(nanoAgentHome);

        environment["USERPROFILE"] = layout.ProfileDir;
        environment["HOME"] = layout.ProfileDir;
        environment["APPDATA"] = layout.RoamingDir;
        environment["LOCALAPPDATA"] = layout.LocalAppDataDir;
        environment["TEMP"] = layout.TempDir;
        environment["TMP"] = layout.TempDir;
        environment["NPM_CONFIG_CACHE"] = layout.NpmCacheDir;
        environment["COREPACK_HOME"] = layout.CorepackHomeDir;
        if (!string.IsNullOrWhiteSpace(windowsDir))
        {
            environment["SystemRoot"] = windowsDir;
            environment["windir"] = windowsDir;
            environment["ComSpec"] = Path.Combine(systemDir, "cmd.exe");
        }

        string pathValue = environment.TryGetValue("PATH", out string? existingPath) && !string.IsNullOrWhiteSpace(existingPath)
            ? existingPath
            : string.Empty;
        string[] requiredPathPrefixes = [systemDir, windowsDir];
        foreach (string prefix in requiredPathPrefixes.Reverse())
        {
            if (!string.IsNullOrWhiteSpace(prefix) &&
                !pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Any(existing => string.Equals(
                        existing.Trim(),
                        prefix,
                        StringComparison.OrdinalIgnoreCase)))
            {
                pathValue = string.IsNullOrWhiteSpace(pathValue)
                    ? prefix
                    : prefix + ";" + pathValue;
            }
        }

        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            environment["Path"] = pathValue;
        }

        environment.TryAdd("PATHEXT", ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC");
        environment["PSModulePath"] = BuildPathVariable(
            environment.TryGetValue("PSModulePath", out string? existingPsModulePath)
                ? existingPsModulePath
                : string.Empty,
            [
                layout.WindowsPowerShellModulesDir,
                layout.PowerShellModulesDir
            ]);

        string? root = Path.GetPathRoot(layout.ProfileDir);
        if (!string.IsNullOrWhiteSpace(root) && root!.Length >= 2)
        {
            environment["HOMEDRIVE"] = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            environment["HOMEPATH"] = layout.ProfileDir[root.Length..];
        }
    }

    internal static async Task<ProcessExecutionResult> ExecuteWithStartupRetryAsync(
        Func<CancellationToken, Task<ProcessExecutionResult>> launcher,
        string nanoAgentHome,
        CancellationToken cancellationToken)
    {
        TimeSpan[] retryDelays =
        [
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromSeconds(2)
        ];

        ProcessExecutionResult result = new(0, string.Empty, string.Empty);
        for (int attempt = 0; attempt < retryDelays.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                EnsureSandboxRuntimeLayout(nanoAgentHome);
                WindowsSandboxLog.Write(
                    nanoAgentHome,
                    $"command startup retry: attempt={attempt + 1}, previous_exit_code={result.ExitCode}, stdout_length={result.StandardOutput.Length}, stderr_length={result.StandardError.Length}, delay_ms={(int)retryDelays[attempt].TotalMilliseconds}");
                if (retryDelays[attempt] > TimeSpan.Zero)
                {
                    await Task.Delay(retryDelays[attempt], cancellationToken);
                }
            }

            result = await launcher(cancellationToken);
            if (result.ExitCode != StatusDllInitFailed)
            {
                return result;
            }
        }

        return result;
    }

    internal static WindowsSandboxRuntimeLayout EnsureSandboxRuntimeLayout(string nanoAgentHome)
    {
        WindowsSandboxPaths.EnsureStateDirectories(nanoAgentHome);

        string runtimeDir = WindowsSandboxPaths.SandboxRuntimeDir(nanoAgentHome);
        string profileDir = WindowsSandboxPaths.SandboxRuntimeProfileDir(nanoAgentHome);
        string tempDir = WindowsSandboxPaths.SandboxRuntimeTempDir(nanoAgentHome);
        string appDataDir = Path.Combine(profileDir, "AppData");
        string roamingDir = Path.Combine(appDataDir, "Roaming");
        string localAppDataDir = Path.Combine(appDataDir, "Local");
        string documentsDir = Path.Combine(profileDir, "Documents");
        string desktopDir = Path.Combine(profileDir, "Desktop");
        string roamingMicrosoftDir = Path.Combine(roamingDir, "Microsoft");
        string roamingWindowsPowerShellDir = Path.Combine(roamingMicrosoftDir, "Windows", "PowerShell");
        string roamingPowerShellDir = Path.Combine(roamingMicrosoftDir, "PowerShell");
        string roamingCryptoDir = Path.Combine(roamingMicrosoftDir, "Crypto");
        string roamingProtectDir = Path.Combine(roamingMicrosoftDir, "Protect");
        string roamingCredentialsDir = Path.Combine(roamingMicrosoftDir, "Credentials");
        string localMicrosoftDir = Path.Combine(localAppDataDir, "Microsoft");
        string localWindowsPowerShellDir = Path.Combine(localMicrosoftDir, "Windows", "PowerShell");
        string localPowerShellDir = Path.Combine(localMicrosoftDir, "PowerShell");
        string localCredentialsDir = Path.Combine(localMicrosoftDir, "Credentials");
        string npmCacheDir = Path.Combine(localAppDataDir, "npm-cache");
        string npmRoamingDir = Path.Combine(roamingDir, "npm");
        string corepackHomeDir = Path.Combine(localAppDataDir, "Corepack");
        string windowsPowerShellModulesDir = Path.Combine(documentsDir, "WindowsPowerShell", "Modules");
        string powerShellModulesDir = Path.Combine(documentsDir, "PowerShell", "Modules");

        string[] writableDirectories =
        [
            runtimeDir,
            profileDir,
            tempDir,
            appDataDir,
            roamingDir,
            localAppDataDir,
            documentsDir,
            desktopDir,
            roamingMicrosoftDir,
            roamingWindowsPowerShellDir,
            roamingPowerShellDir,
            roamingCryptoDir,
            roamingProtectDir,
            roamingCredentialsDir,
            localMicrosoftDir,
            localWindowsPowerShellDir,
            localPowerShellDir,
            localCredentialsDir,
            npmCacheDir,
            npmRoamingDir,
            corepackHomeDir,
            windowsPowerShellModulesDir,
            powerShellModulesDir
        ];

        foreach (string path in writableDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(path);
        }

        return new WindowsSandboxRuntimeLayout(
            runtimeDir,
            profileDir,
            tempDir,
            roamingDir,
            localAppDataDir,
            documentsDir,
            desktopDir,
            npmCacheDir,
            corepackHomeDir,
            windowsPowerShellModulesDir,
            powerShellModulesDir,
            writableDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string BuildPathVariable(
        string existingValue,
        IEnumerable<string> requiredPrefixes)
    {
        string pathValue = existingValue;
        foreach (string prefix in requiredPrefixes.Reverse())
        {
            if (!string.IsNullOrWhiteSpace(prefix) &&
                !pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Any(existing => string.Equals(
                        existing.Trim(),
                        prefix,
                        StringComparison.OrdinalIgnoreCase)))
            {
                pathValue = string.IsNullOrWhiteSpace(pathValue)
                    ? prefix
                    : prefix + ";" + pathValue;
            }
        }

        return pathValue;
    }

    private static string CanonicalizeWindowsEnvironmentKey(string key)
    {
        return key.ToUpperInvariant() switch
        {
            "PATH" => "Path",
            "PATHEXT" => "PATHEXT",
            "SYSTEMROOT" => "SystemRoot",
            "WINDIR" => "windir",
            "COMSPEC" => "ComSpec",
            "USERPROFILE" => "USERPROFILE",
            "HOME" => "HOME",
            "APPDATA" => "APPDATA",
            "LOCALAPPDATA" => "LOCALAPPDATA",
            "TEMP" => "TEMP",
            "TMP" => "TMP",
            "HOMEDRIVE" => "HOMEDRIVE",
            "HOMEPATH" => "HOMEPATH",
            "PSMODULEPATH" => "PSModulePath",
            "NPM_CONFIG_CACHE" => "NPM_CONFIG_CACHE",
            "COREPACK_HOME" => "COREPACK_HOME",
            _ => key
        };
    }

    private static ProcessExecutionRequest MaterializeExternalExecutableIfNeeded(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context)
    {
        if (!Path.IsPathFullyQualified(request.FileName) || !File.Exists(request.FileName))
        {
            return request;
        }

        string executablePath = Path.GetFullPath(request.FileName);
        if (IsUnderPath(executablePath, context.CommandCwd) ||
            IsUnderPath(executablePath, context.PolicyCwd) ||
            context.WritableRoots.Any(root => IsUnderPath(executablePath, root)))
        {
            return request;
        }

        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (IsUnderPath(executablePath, windowsDirectory) ||
            IsUnderPath(executablePath, context.NanoAgentHome))
        {
            return request;
        }

        string materializedPath = WindowsSandboxHelperMaterializer.MaterializeExternalExecutable(
            context.NanoAgentHome,
            executablePath);
        return request with { FileName = materializedPath };
    }

    private static bool IsUnderPath(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void PrepareAcls(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        WindowsCapabilitySids capabilitySids,
        string workspaceSid,
        string sandboxGroupSid,
        WindowsAllowDenyPaths paths,
        List<(string Path, string Sid, FileSystemRights Rights, AccessControlType Type)> temporaryAces)
    {
        FileSystemRights readExecute = FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory;
        WindowsSandboxAcl.AllowNulDevice(capabilitySids.Readonly);
        PrepareRuntimeProfileAcls(context.NanoAgentHome, capabilitySids, sandboxGroupSid, temporaryAces);
        WindowsSandboxAcl.AddAllowAce(context.CommandCwd, sandboxGroupSid, readExecute);
        WindowsSandboxAcl.AddAllowAce(context.CommandCwd, capabilitySids.Readonly, readExecute);
        temporaryAces.Add((context.CommandCwd, sandboxGroupSid, readExecute, AccessControlType.Allow));
        temporaryAces.Add((context.CommandCwd, capabilitySids.Readonly, readExecute, AccessControlType.Allow));
        // Grant explicit read/execute to platform default system directories.
        // These are already readable by Everyone/Users - the explicit ACEs are
        // defense-in-depth for restricted tokens that strip inherited group memberships.
        // If the caller lacks WRITE_DAC (e.g. non-elevated test host), skip gracefully.
        foreach (string platformRoot in WindowsSandboxSetupRoots.PlatformDefaultReadRoots)
        {
            if (!Directory.Exists(platformRoot) && !File.Exists(platformRoot)) continue;
            try
            {
                WindowsSandboxAcl.AddAllowAce(platformRoot, sandboxGroupSid, readExecute);
                temporaryAces.Add((platformRoot, sandboxGroupSid, readExecute, AccessControlType.Allow));
            }
            catch (Exception aceException)
            {
                WindowsSandboxLog.Write(context.NanoAgentHome,
                    $"skipped platform root ACE for {platformRoot}: {aceException.GetType().Name}: {aceException.Message}");
            }
        }

        if (context.Mode != ToolSandboxMode.WorkspaceWrite)
        {
            return;
        }

        foreach (string path in paths.Allow)
        {
            FileSystemRights writeRights = FileSystemRights.Modify | FileSystemRights.ReadAndExecute;
            string sid = string.Equals(
                WindowsCapabilitySidStore.CanonicalPathKey(path),
                WindowsCapabilitySidStore.CanonicalPathKey(context.CommandCwd),
                StringComparison.OrdinalIgnoreCase)
                ? workspaceSid
                : capabilitySids.Workspace;
            WindowsSandboxAcl.AddAllowAce(path, sandboxGroupSid, writeRights);
            WindowsSandboxAcl.AddAllowAce(path, sid, writeRights);
            temporaryAces.Add((path, sandboxGroupSid, writeRights, AccessControlType.Allow));
            temporaryAces.Add((path, sid, writeRights, AccessControlType.Allow));
        }

        foreach (string path in paths.Deny)
        {
            WindowsSandboxAcl.AddDenyWriteAce(path, workspaceSid);
            WindowsSandboxAcl.AddDenyWriteAce(path, capabilitySids.Workspace);
        }
    }

    private static void PrepareRuntimeProfileAcls(
        string nanoAgentHome,
        WindowsCapabilitySids capabilitySids,
        string sandboxGroupSid,
        List<(string Path, string Sid, FileSystemRights Rights, AccessControlType Type)> temporaryAces)
    {
        WindowsSandboxRuntimeLayout layout = EnsureSandboxRuntimeLayout(nanoAgentHome);
        string sandboxDir = WindowsSandboxPaths.SandboxDir(nanoAgentHome);
        string sandboxSecretsDir = WindowsSandboxPaths.SandboxSecretsDir(nanoAgentHome);

        (string Path, FileSystemRights Rights)[] paths =
        [
            (nanoAgentHome, FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory),
            (sandboxDir, FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory),
            (sandboxSecretsDir, FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory),
            .. layout.WritableDirectories.Select(static path => (path, FileSystemRights.Modify | FileSystemRights.ReadAndExecute))
        ];
        string[] readableFiles =
        [
            WindowsSandboxPaths.CapSidFile(nanoAgentHome),
            WindowsSandboxPaths.SetupMarkerPath(nanoAgentHome),
            WindowsSandboxPaths.SandboxUsersPath(nanoAgentHome)
        ];

        string offlineUserSid = ResolveAccountSid(WindowsSandboxPaths.OfflineUsername, ".").Value;
        string onlineUserSid = ResolveAccountSid(WindowsSandboxPaths.OnlineUsername, ".").Value;
        string[] sids =
        [
            sandboxGroupSid,
            offlineUserSid,
            onlineUserSid,
            capabilitySids.Readonly,
            capabilitySids.Workspace
        ];
        foreach ((string path, FileSystemRights rights) in paths)
        {
            foreach (string sid in sids)
            {
                WindowsSandboxAcl.AddAllowAce(path, sid, rights);
                temporaryAces.Add((path, sid, rights, AccessControlType.Allow));
            }
        }

        const FileSystemRights fileReadRights = FileSystemRights.ReadAndExecute;
        foreach (string filePath in readableFiles.Where(File.Exists))
        {
            foreach (string sid in sids)
            {
                WindowsSandboxAcl.AddAllowAce(filePath, sid, fileReadRights);
                temporaryAces.Add((filePath, sid, fileReadRights, AccessControlType.Allow));
            }
        }
    }

    internal static string[] SandboxGroupSids()
    {
        return WindowsSandboxPaths.SandboxGroupNames()
            .Select(groupName => new System.Security.Principal.NTAccount(
                    Environment.MachineName,
                    groupName)
                .Translate(typeof(System.Security.Principal.SecurityIdentifier))
                .Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<ProcessExecutionResult> LaunchWithCreateProcessAsUserAsync(
        ProcessExecutionRequest request,
        WindowsSandboxToken token,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken)
    {
        WindowsSandboxNative.SecurityAttributes inheritablePipeAttributes = new()
        {
            nLength = Marshal.SizeOf<WindowsSandboxNative.SecurityAttributes>(),
            bInheritHandle = true
        };

        if (!WindowsSandboxNative.CreatePipe(out SafeFileHandle stdinRead, out SafeFileHandle stdinWrite, ref inheritablePipeAttributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe stdin failed.");
        }

        if (!WindowsSandboxNative.CreatePipe(out SafeFileHandle stdoutRead, out SafeFileHandle stdoutWrite, ref inheritablePipeAttributes, 0))
        {
            stdinRead.Dispose();
            stdinWrite.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe stdout failed.");
        }

        if (!WindowsSandboxNative.CreatePipe(out SafeFileHandle stderrRead, out SafeFileHandle stderrWrite, ref inheritablePipeAttributes, 0))
        {
            stdinRead.Dispose();
            stdinWrite.Dispose();
            stdoutRead.Dispose();
            stdoutWrite.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe stderr failed.");
        }

        IntPtr environmentBlock = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr handleList = IntPtr.Zero;
        WindowsSandboxNative.ProcessInformation processInformation = default;
        using WindowsSandboxDesktop desktop = WindowsSandboxDesktop.Prepare(context.UsePrivateDesktop);

        try
        {
            WindowsSandboxNative.SetHandleInformation(stdinWrite, HandleFlagInherit, 0);
            WindowsSandboxNative.SetHandleInformation(stdoutRead, HandleFlagInherit, 0);
            WindowsSandboxNative.SetHandleInformation(stderrRead, HandleFlagInherit, 0);

            byte[] environmentBytes = BuildEnvironmentBlockBytes(request.EnvironmentVariables, context.NanoAgentHome);
            environmentBlock = Marshal.AllocHGlobal(environmentBytes.Length);
            Marshal.Copy(environmentBytes, 0, environmentBlock, environmentBytes.Length);

            SafeFileHandle[] inheritedHandles = [stdinRead, stdoutWrite, stderrWrite];
            attributeList = CreateHandleListAttribute(inheritedHandles, out handleList);
            WindowsSandboxNative.StartupInfoEx startupInfo = new()
            {
                StartupInfo =
                {
                    cb = Marshal.SizeOf<WindowsSandboxNative.StartupInfoEx>(),
                    dwFlags = WindowsSandboxNative.StartfUseStdHandles,
                    hStdInput = stdinRead.DangerousGetHandle(),
                    hStdOutput = stdoutWrite.DangerousGetHandle(),
                    hStdError = stderrWrite.DangerousGetHandle(),
                    lpDesktop = desktop.DesktopName
                },
                lpAttributeList = attributeList
            };

            string commandLineText = BuildWindowsCommandLine(request);
            WindowsSandboxLog.Write(context.NanoAgentHome, $"restricted child launch: desktop={desktop.DesktopName}");
            processInformation = CreateSandboxedProcess(
                token,
                commandLineText,
                request.WorkingDirectory ?? context.CommandCwd,
                environmentBlock,
                startupInfo,
                context.NanoAgentHome);

            stdinRead.Dispose();
            stdoutWrite.Dispose();
            stderrWrite.Dispose();

            Task<string> stdoutTask = ReadToEndCappedAsync(stdoutRead, request.MaxOutputCharacters, cancellationToken);
            Task<string> stderrTask = ReadToEndCappedAsync(stderrRead, request.MaxOutputCharacters, cancellationToken);
            Task stdinTask = WriteStandardInputAsync(stdinWrite, request.StandardInput, cancellationToken);

            int exitCode = await WaitForExitAsync(processInformation.hProcess, cancellationToken);
            await stdinTask;
            return new ProcessExecutionResult(exitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            if (processInformation.hProcess != IntPtr.Zero)
            {
                WindowsSandboxNative.TerminateProcess(processInformation.hProcess, 1);
            }

            throw;
        }
        finally
        {
            stdinRead.Dispose();
            stdinWrite.Dispose();
            stdoutRead.Dispose();
            stdoutWrite.Dispose();
            stderrRead.Dispose();
            stderrWrite.Dispose();
            FreeAttributeList(attributeList);
            if (handleList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(handleList);
            }

            if (environmentBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentBlock);
            }

            if (processInformation.hThread != IntPtr.Zero)
            {
                WindowsSandboxNative.CloseHandle(processInformation.hThread);
            }

            if (processInformation.hProcess != IntPtr.Zero)
            {
                WindowsSandboxNative.CloseHandle(processInformation.hProcess);
            }
        }
    }

    private static IntPtr CreateHandleListAttribute(SafeFileHandle[] handles, out IntPtr handleList)
    {
        IntPtr size = IntPtr.Zero;
        _ = WindowsSandboxNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        if (size == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList size query failed.");
        }

        IntPtr attributeList = Marshal.AllocHGlobal(size);
        bool initialized = false;
        handleList = IntPtr.Zero;
        try
        {
            if (!WindowsSandboxNative.InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed.");
            }

            initialized = true;
            handleList = Marshal.AllocHGlobal(IntPtr.Size * handles.Length);
            for (int index = 0; index < handles.Length; index++)
            {
                Marshal.WriteIntPtr(handleList, index * IntPtr.Size, handles[index].DangerousGetHandle());
            }

            if (!WindowsSandboxNative.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    WindowsSandboxNative.ProcThreadAttributeHandleList,
                    handleList,
                    (IntPtr)(IntPtr.Size * handles.Length),
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute handle list failed.");
            }

            return attributeList;
        }
        catch
        {
            if (initialized)
            {
                WindowsSandboxNative.DeleteProcThreadAttributeList(attributeList);
            }

            Marshal.FreeHGlobal(attributeList);
            if (handleList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(handleList);
                handleList = IntPtr.Zero;
            }

            throw;
        }
    }

    private static WindowsSandboxNative.ProcessInformation CreateSandboxedProcess(
        WindowsSandboxToken token,
        string commandLineText,
        string workingDirectory,
        IntPtr environmentBlock,
        WindowsSandboxNative.StartupInfoEx startupInfo,
        string? nanoAgentHome = null)
    {
        StringBuilder commandLine = new(commandLineText, commandLineText.Length + 1);
        bool created = WindowsSandboxNative.CreateProcessAsUser(
            token.Handle,
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            true,
            RestrictedChildCreationFlags(includeExtendedStartupInfo: true),
            environmentBlock,
            workingDirectory,
            ref startupInfo,
            out WindowsSandboxNative.ProcessInformation processInformation);
        if (created)
        {
            if (!string.IsNullOrWhiteSpace(nanoAgentHome))
            {
                WindowsSandboxLog.Write(nanoAgentHome, "restricted child launch path: CreateProcessAsUser");
            }

            return processInformation;
        }

        int createProcessAsUserError = Marshal.GetLastWin32Error();
        if ((uint)createProcessAsUserError != WindowsSandboxNative.ErrorPrivilegeNotHeld)
        {
            throw new Win32Exception(
                createProcessAsUserError,
                $"CreateProcessAsUserW failed with Win32 {createProcessAsUserError} for '{commandLineText}'.");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(nanoAgentHome))
            {
                WindowsSandboxLog.Write(nanoAgentHome, $"restricted child launch path: CreateProcessWithTokenW fallback after {createProcessAsUserError}");
            }

            return CreateSandboxedProcessWithToken(
                token,
                commandLineText,
                workingDirectory,
                environmentBlock,
                startupInfo.StartupInfo);
        }
        catch (Win32Exception fallbackException)
        {
            throw new Win32Exception(
                fallbackException.NativeErrorCode,
                $"CreateProcessAsUserW failed with Win32 {createProcessAsUserError}; CreateProcessWithTokenW fallback failed with Win32 {fallbackException.NativeErrorCode} for '{commandLineText}'.");
        }
    }

    private static WindowsSandboxNative.ProcessInformation CreateSandboxedProcessWithToken(
        WindowsSandboxToken token,
        string commandLineText,
        string workingDirectory,
        IntPtr environmentBlock,
        WindowsSandboxNative.StartupInfo startupInfo)
    {
        StringBuilder commandLine = new(commandLineText, commandLineText.Length + 1);
        bool created = WindowsSandboxNative.CreateProcessWithTokenW(
            token.Handle,
            0,
            null,
            commandLine,
            RestrictedChildCreationFlags(includeExtendedStartupInfo: false),
            environmentBlock,
            workingDirectory,
            ref startupInfo,
            out WindowsSandboxNative.ProcessInformation processInformation);
        if (!created)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                error,
                $"CreateProcessWithTokenW failed with Win32 {error} for '{commandLineText}'.");
        }

        return processInformation;
    }

    internal static uint RestrictedChildCreationFlags(bool includeExtendedStartupInfo)
    {
        uint flags = WindowsSandboxNative.CreateUnicodeEnvironment;
        if (includeExtendedStartupInfo)
        {
            flags |= WindowsSandboxNative.ExtendedStartupInfoPresent;
        }

        return flags;
    }

    private static void FreeAttributeList(IntPtr attributeList)
    {
        if (attributeList == IntPtr.Zero)
        {
            return;
        }

        WindowsSandboxNative.DeleteProcThreadAttributeList(attributeList);
        Marshal.FreeHGlobal(attributeList);
    }

    private static async Task<int> WaitForExitAsync(IntPtr processHandle, CancellationToken cancellationToken)
    {
        while (true)
        {
            uint waitResult = WindowsSandboxNative.WaitForSingleObject(processHandle, 50);
            if (waitResult == WindowsSandboxNative.WaitObject0)
            {
                if (!WindowsSandboxNative.GetExitCodeProcess(processHandle, out uint exitCode))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeProcess failed.");
                }

                return unchecked((int)exitCode);
            }

            if (waitResult == WindowsSandboxNative.WaitFailed)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject failed.");
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private static async Task WriteStandardInputAsync(
        SafeFileHandle stdinWrite,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(stdinWrite, FileAccess.Write, BufferSize, isAsync: false);
        if (!string.IsNullOrEmpty(standardInput))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(standardInput);
            await stream.WriteAsync(bytes, cancellationToken);
        }
    }

    private static async Task RunPipeRunnerAsync(
        string pipeIn,
        string pipeOut,
        CancellationToken cancellationToken)
    {
        using NamedPipeClientStream inboundClient = WindowsSandboxRunnerClient.CreateInboundClient(pipeIn);
        using NamedPipeClientStream outboundClient = WindowsSandboxRunnerClient.CreateOutboundClient(pipeOut);
        await Task.WhenAll(
            inboundClient.ConnectAsync(cancellationToken),
            outboundClient.ConnectAsync(cancellationToken));

        WindowsSandboxIpcMessage requestMessage = await WindowsSandboxFramedIpc.ReadAsync(inboundClient, cancellationToken);
        if (requestMessage.Kind != WindowsSandboxIpcMessageKind.SpawnRequest ||
            string.IsNullOrWhiteSpace(requestMessage.PayloadBase64))
        {
            throw new InvalidOperationException("Windows sandbox runner received an invalid spawn request.");
        }

        WindowsSandboxRunnerPayload payload = DecodeRunnerPayload(requestMessage.PayloadBase64);
        await WindowsSandboxFramedIpc.WriteAsync(
            outboundClient,
            new WindowsSandboxIpcMessage
            {
                Kind = WindowsSandboxIpcMessageKind.SpawnReady
            },
            cancellationToken);

        try
        {
            ProcessExecutionRequest request = new(
                payload.FileName,
                payload.Arguments,
                payload.StandardInput,
                payload.WorkingDirectory,
                payload.MaxOutputCharacters,
                payload.EnvironmentVariables);
            ToolSandboxMode mode = payload.Mode == ToolSandboxModeDto.WorkspaceWrite
                ? ToolSandboxMode.WorkspaceWrite
                : ToolSandboxMode.ReadOnly;
            WindowsSandboxExecutionContext context = new(
                mode,
                payload.NanoAgentHome,
                payload.CommandCwd,
                payload.CommandCwd,
                [],
                IncludeTempEnvironmentVariables: true,
                UsePrivateDesktop: payload.UsePrivateDesktop);

            ProcessExecutionResult result = await RunSandboxUserChildAsync(request, context, cancellationToken);
            await WindowsSandboxFramedIpc.WriteAsync(
                outboundClient,
                new WindowsSandboxIpcMessage
                {
                    Kind = WindowsSandboxIpcMessageKind.Exit,
                    ExitCode = result.ExitCode,
                    PayloadBase64 = EncodeRunnerResult(
                        new WindowsSandboxRunnerResult
                        {
                            ExitCode = result.ExitCode,
                            Stdout = result.StandardOutput,
                            Stderr = result.StandardError
                        })
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            await WindowsSandboxFramedIpc.WriteAsync(
                outboundClient,
                new WindowsSandboxIpcMessage
                {
                    Kind = WindowsSandboxIpcMessageKind.Error,
                    Error = $"{exception.GetType().Name}: {exception.Message}"
                },
                cancellationToken);
        }
    }

    private static Task<ProcessExecutionResult> RunSandboxUserChildAsync(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken)
    {
        // The helper already runs as the dedicated sandbox user with ACLs prepared for the
        // selected policy. Launch the child directly under that identity so Node/PowerShell do
        // not need to survive an extra nested restricted-token initialization step.
        ProcessExecutionRequest sandboxedRequest = request with
        {
            EnvironmentVariables = BuildSandboxEnvironmentMap(
                request.EnvironmentVariables,
                context.NanoAgentHome)
        };

        WindowsSandboxLog.Write(context.NanoAgentHome, "restricted child launch path: sandbox-user-direct");
        return new ProcessRunner().RunAsync(
            sandboxedRequest,
            cancellationToken);
    }

    private static async Task<string> ReadToEndCappedAsync(
        SafeFileHandle handle,
        int? maxCharacters,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(handle, FileAccess.Read, BufferSize, isAsync: false);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        char[] buffer = new char[BufferSize];
        StringBuilder builder = new();
        bool truncated = false;
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (maxCharacters is null)
            {
                builder.Append(buffer, 0, read);
                continue;
            }

            int remaining = maxCharacters.Value - builder.Length;
            if (remaining <= 0)
            {
                truncated = true;
                continue;
            }

            int take = Math.Min(remaining, read);
            builder.Append(buffer, 0, take);
            truncated |= take < read;
        }

        if (truncated && maxCharacters is > 3)
        {
            builder.Length = maxCharacters.Value - 3;
            builder.Append("...");
        }

        return builder.ToString();
    }

    internal static string QuoteWindowsArgument(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (!value.Any(static character => char.IsWhiteSpace(character) || character == '"'))
        {
            return value;
        }

        StringBuilder builder = new();
        builder.Append('"');
        int backslashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            backslashes = 0;
            builder.Append(character);
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    internal sealed record WindowsSandboxRuntimeLayout(
        string RuntimeDir,
        string ProfileDir,
        string TempDir,
        string RoamingDir,
        string LocalAppDataDir,
        string DocumentsDir,
        string DesktopDir,
        string NpmCacheDir,
        string CorepackHomeDir,
        string WindowsPowerShellModulesDir,
        string PowerShellModulesDir,
        IReadOnlyList<string> WritableDirectories);
}
