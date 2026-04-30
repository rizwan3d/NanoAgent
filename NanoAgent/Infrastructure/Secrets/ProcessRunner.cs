using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NanoAgent.Infrastructure.Secrets;

internal sealed class ProcessRunner : IProcessRunner
{
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint StartFUseStdHandles = 0x00000100;
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;
    private const uint WaitFailed = 0xFFFFFFFF;
    private static readonly IntPtr ProcThreadAttributePseudoConsole = 0x00020016;
    private static readonly IntPtr ProcThreadAttributeSecurityCapabilities = 0x00020009;

    public async Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.WindowsSandbox is not null)
        {
            return await RunWithWindowsAppContainerAsync(
                request,
                cancellationToken);
        }

        if (request.UsePseudoTerminal)
        {
            return await RunWithPseudoTerminalAsync(
                request,
                cancellationToken);
        }

        return await RunDirectAsync(
            request,
            cancellationToken);
    }

    public IBackgroundProcess StartBackground(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.UsePseudoTerminal)
        {
            throw new PlatformNotSupportedException(
                "Background process execution does not support pseudo terminals.");
        }

        if (request.WindowsSandbox is not null)
        {
            return StartWindowsAppContainerBackground(request);
        }

        return StartDirectBackground(request);
    }

    private async Task<ProcessExecutionResult> RunDirectAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = request.FileName,
            RedirectStandardInput = request.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (KeyValuePair<string, string> environmentVariable in request.EnvironmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(environmentVariable.Key))
                {
                    startInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
                }
            }
        }

        using Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();

        Task<string> standardOutputTask = ReadToEndCappedAsync(
            process.StandardOutput,
            request.MaxOutputCharacters,
            cancellationToken);
        Task<string> standardErrorTask = ReadToEndCappedAsync(
            process.StandardError,
            request.MaxOutputCharacters,
            cancellationToken);

        if (request.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }

        return new ProcessExecutionResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static IBackgroundProcess StartDirectBackground(ProcessExecutionRequest request)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = request.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (KeyValuePair<string, string> environmentVariable in request.EnvironmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(environmentVariable.Key))
                {
                    startInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
                }
            }
        }

        Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            process.Start();
            return new SystemBackgroundProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private async Task<ProcessExecutionResult> RunWithPseudoTerminalAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return await RunWithWindowsPseudoTerminalAsync(
                    request,
                    cancellationToken);
            }
            catch (EntryPointNotFoundException exception)
            {
                throw new PlatformNotSupportedException(
                    "Windows ConPTY is not available on this version of Windows.",
                    exception);
            }
        }

        if (OperatingSystem.IsLinux())
        {
            return await RunWithLinuxPseudoTerminalAsync(
                request,
                cancellationToken);
        }

        throw new PlatformNotSupportedException(
            "PTY process execution is not supported on this platform.");
    }

    private async Task<ProcessExecutionResult> RunWithWindowsAppContainerAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows AppContainer sandbox execution is only supported on Windows.");
        }

        IntPtr appContainerSid = PrepareWindowsAppContainer(
            request.WindowsSandbox
                ?? throw new InvalidOperationException("Windows sandbox configuration is missing."));
        try
        {
            if (request.UsePseudoTerminal)
            {
                return await RunWithWindowsPseudoTerminalAsync(
                    request,
                    cancellationToken,
                    appContainerSid);
            }

            return await RunWindowsAppContainerDirectAsync(
                request,
                appContainerSid,
                cancellationToken);
        }
        finally
        {
            if (appContainerSid != IntPtr.Zero)
            {
                FreeSid(appContainerSid);
            }
        }
    }

    private async Task<ProcessExecutionResult> RunWindowsAppContainerDirectAsync(
        ProcessExecutionRequest request,
        IntPtr appContainerSid,
        CancellationToken cancellationToken)
    {
        CreateInputPipe(out SafeFileHandle stdinRead, out SafeFileHandle stdinWrite);
        CreateOutputPipe(out SafeFileHandle stdoutRead, out SafeFileHandle stdoutWrite);
        CreateOutputPipe(out SafeFileHandle stderrRead, out SafeFileHandle stderrWrite);

        IntPtr attributeList = IntPtr.Zero;
        IntPtr securityCapabilitiesBuffer = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        ProcessInformation processInformation = default;

        try
        {
            securityCapabilitiesBuffer = CreateSecurityCapabilitiesBuffer(appContainerSid);
            attributeList = CreateWindowsStartupAttributeList(
                IntPtr.Zero,
                securityCapabilitiesBuffer);
            environmentBlock = CreateWindowsEnvironmentBlock(request.EnvironmentVariables);

            StartupInfoEx startupInfo = new()
            {
                StartupInfo =
                {
                    cb = Marshal.SizeOf<StartupInfoEx>(),
                    dwFlags = (int)StartFUseStdHandles,
                    hStdInput = stdinRead.DangerousGetHandle(),
                    hStdOutput = stdoutWrite.DangerousGetHandle(),
                    hStdError = stderrWrite.DangerousGetHandle()
                },
                lpAttributeList = attributeList
            };
            string commandLineText = BuildWindowsCommandLine(request);
            StringBuilder commandLine = new(commandLineText, commandLineText.Length + 1);

            bool created = CreateProcessW(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                true,
                ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                environmentBlock,
                string.IsNullOrWhiteSpace(request.WorkingDirectory) ? null : request.WorkingDirectory,
                ref startupInfo,
                out processInformation);
            if (!created)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            stdinRead.Dispose();
            stdoutWrite.Dispose();
            stderrWrite.Dispose();

            await using FileStream stdoutStream = new(
                stdoutRead,
                FileAccess.Read,
                bufferSize: 4096,
                isAsync: true);
            await using FileStream stderrStream = new(
                stderrRead,
                FileAccess.Read,
                bufferSize: 4096,
                isAsync: true);
            using StreamReader stdoutReader = new(
                stdoutStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false);
            using StreamReader stderrReader = new(
                stderrStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false);
            Task<string> standardOutputTask = ReadToEndCappedAsync(
                stdoutReader,
                request.MaxOutputCharacters,
                cancellationToken);
            Task<string> standardErrorTask = ReadToEndCappedAsync(
                stderrReader,
                request.MaxOutputCharacters,
                cancellationToken);

            await using (FileStream stdinStream = new(
                             stdinWrite,
                             FileAccess.Write,
                             bufferSize: 4096,
                             isAsync: true))
            {
                if (request.StandardInput is not null)
                {
                    byte[] inputBytes = Encoding.UTF8.GetBytes(request.StandardInput);
                    await stdinStream.WriteAsync(inputBytes, cancellationToken);
                    await stdinStream.FlushAsync(cancellationToken);
                }
            }

            int exitCode;
            try
            {
                exitCode = await WaitForWindowsProcessExitAsync(
                    processInformation.hProcess,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryTerminateWindowsProcess(processInformation.hProcess);
                throw;
            }

            return new ProcessExecutionResult(
                exitCode,
                await standardOutputTask,
                await standardErrorTask);
        }
        catch
        {
            if (processInformation.hProcess != IntPtr.Zero)
            {
                TryTerminateWindowsProcess(processInformation.hProcess);
            }

            stdinRead.Dispose();
            stdinWrite.Dispose();
            stdoutRead.Dispose();
            stdoutWrite.Dispose();
            stderrRead.Dispose();
            stderrWrite.Dispose();
            throw;
        }
        finally
        {
            FreePseudoConsoleAttributeList(attributeList);

            if (securityCapabilitiesBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(securityCapabilitiesBuffer);
            }

            if (environmentBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentBlock);
            }

            if (processInformation.hThread != IntPtr.Zero)
            {
                CloseHandle(processInformation.hThread);
            }

            if (processInformation.hProcess != IntPtr.Zero)
            {
                CloseHandle(processInformation.hProcess);
            }
        }
    }

    private static IBackgroundProcess StartWindowsAppContainerBackground(
        ProcessExecutionRequest request)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows AppContainer sandbox execution is only supported on Windows.");
        }

        IntPtr appContainerSid = PrepareWindowsAppContainer(
            request.WindowsSandbox
                ?? throw new InvalidOperationException("Windows sandbox configuration is missing."));
        try
        {
            return StartWindowsAppContainerBackgroundProcess(
                request,
                appContainerSid);
        }
        finally
        {
            if (appContainerSid != IntPtr.Zero)
            {
                FreeSid(appContainerSid);
            }
        }
    }

    private static IBackgroundProcess StartWindowsAppContainerBackgroundProcess(
        ProcessExecutionRequest request,
        IntPtr appContainerSid)
    {
        CreateInputPipe(out SafeFileHandle stdinRead, out SafeFileHandle stdinWrite);
        CreateOutputPipe(out SafeFileHandle stdoutRead, out SafeFileHandle stdoutWrite);
        CreateOutputPipe(out SafeFileHandle stderrRead, out SafeFileHandle stderrWrite);

        IntPtr attributeList = IntPtr.Zero;
        IntPtr securityCapabilitiesBuffer = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        ProcessInformation processInformation = default;

        try
        {
            securityCapabilitiesBuffer = CreateSecurityCapabilitiesBuffer(appContainerSid);
            attributeList = CreateWindowsStartupAttributeList(
                IntPtr.Zero,
                securityCapabilitiesBuffer);
            environmentBlock = CreateWindowsEnvironmentBlock(request.EnvironmentVariables);

            StartupInfoEx startupInfo = new()
            {
                StartupInfo =
                {
                    cb = Marshal.SizeOf<StartupInfoEx>(),
                    dwFlags = (int)StartFUseStdHandles,
                    hStdInput = stdinRead.DangerousGetHandle(),
                    hStdOutput = stdoutWrite.DangerousGetHandle(),
                    hStdError = stderrWrite.DangerousGetHandle()
                },
                lpAttributeList = attributeList
            };
            string commandLineText = BuildWindowsCommandLine(request);
            StringBuilder commandLine = new(commandLineText, commandLineText.Length + 1);

            bool created = CreateProcessW(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                true,
                ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                environmentBlock,
                string.IsNullOrWhiteSpace(request.WorkingDirectory) ? null : request.WorkingDirectory,
                ref startupInfo,
                out processInformation);
            if (!created)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            stdinRead.Dispose();
            stdoutWrite.Dispose();
            stderrWrite.Dispose();
            stdinWrite.Dispose();

            IBackgroundProcess process = new WindowsBackgroundProcess(
                stdoutRead,
                stderrRead,
                processInformation.hProcess,
                processInformation.hThread,
                processInformation.dwProcessId);
            processInformation = default;
            return process;
        }
        catch
        {
            if (processInformation.hProcess != IntPtr.Zero)
            {
                TryKillWindowsProcessTree(
                    processInformation.dwProcessId,
                    processInformation.hProcess);
            }

            stdinRead.Dispose();
            stdinWrite.Dispose();
            stdoutRead.Dispose();
            stdoutWrite.Dispose();
            stderrRead.Dispose();
            stderrWrite.Dispose();

            if (processInformation.hThread != IntPtr.Zero)
            {
                CloseHandle(processInformation.hThread);
            }

            if (processInformation.hProcess != IntPtr.Zero)
            {
                CloseHandle(processInformation.hProcess);
            }

            throw;
        }
        finally
        {
            FreePseudoConsoleAttributeList(attributeList);

            if (securityCapabilitiesBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(securityCapabilitiesBuffer);
            }

            if (environmentBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentBlock);
            }
        }
    }

    private Task<ProcessExecutionResult> RunWithLinuxPseudoTerminalAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ProcessExecutionRequest scriptRequest = new(
            "script",
            ["-q", "-e", "-c", BuildPosixCommandLine(request), "/dev/null"],
            StandardInput: request.StandardInput,
            WorkingDirectory: request.WorkingDirectory,
            MaxOutputCharacters: request.MaxOutputCharacters,
            EnvironmentVariables: request.EnvironmentVariables);

        return RunDirectAsync(
            scriptRequest,
            cancellationToken);
    }

    private async Task<ProcessExecutionResult> RunWithWindowsPseudoTerminalAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken,
        IntPtr appContainerSid = default)
    {
        using WindowsPseudoTerminalProcess process = StartWindowsPseudoTerminal(
            request,
            appContainerSid);
        await using FileStream outputStream = new(
            process.OutputReader,
            FileAccess.Read,
            bufferSize: 4096,
            isAsync: true);
        using StreamReader outputReader = new(
            outputStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false);
        Task<string> standardOutputTask = ReadToEndCappedAsync(
            outputReader,
            request.MaxOutputCharacters,
            cancellationToken);

        if (request.StandardInput is not null)
        {
            await using FileStream inputStream = new(
                process.InputWriter,
                FileAccess.Write,
                bufferSize: 4096,
                isAsync: true);
            byte[] inputBytes = Encoding.UTF8.GetBytes(request.StandardInput);
            await inputStream.WriteAsync(inputBytes, cancellationToken);
            await inputStream.FlushAsync(cancellationToken);
        }
        else
        {
            process.InputWriter.Dispose();
        }

        int exitCode;
        try
        {
            exitCode = await WaitForWindowsProcessExitAsync(
                process,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.TryKill();
            throw;
        }
        finally
        {
            process.ClosePseudoConsole();
        }

        return new ProcessExecutionResult(
            exitCode,
            await standardOutputTask,
            string.Empty);
    }

    private static WindowsPseudoTerminalProcess StartWindowsPseudoTerminal(
        ProcessExecutionRequest request,
        IntPtr appContainerSid)
    {
        if (!CreatePipe(out SafeFileHandle inputRead, out SafeFileHandle inputWrite, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!CreatePipe(out SafeFileHandle outputRead, out SafeFileHandle outputWrite, IntPtr.Zero, 0))
        {
            inputRead.Dispose();
            inputWrite.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        IntPtr pseudoConsole = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr securityCapabilitiesBuffer = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        ProcessInformation processInformation = default;

        try
        {
            int result = CreatePseudoConsole(
                new Coord(120, 30),
                inputRead,
                outputWrite,
                0,
                out pseudoConsole);
            if (result != 0)
            {
                throw new PlatformNotSupportedException(
                    $"Windows ConPTY could not be created (HRESULT 0x{result:X8}).");
            }

            inputRead.Dispose();
            outputWrite.Dispose();

            if (appContainerSid != IntPtr.Zero)
            {
                securityCapabilitiesBuffer = CreateSecurityCapabilitiesBuffer(appContainerSid);
            }

            attributeList = CreateWindowsStartupAttributeList(
                pseudoConsole,
                securityCapabilitiesBuffer);
            environmentBlock = CreateWindowsEnvironmentBlock(request.EnvironmentVariables);

            StartupInfoEx startupInfo = new()
            {
                StartupInfo =
                {
                    cb = Marshal.SizeOf<StartupInfoEx>()
                },
                lpAttributeList = attributeList
            };
            string commandLineText = BuildWindowsCommandLine(request);
            StringBuilder commandLine = new(commandLineText, commandLineText.Length + 1);

            bool created = CreateProcessW(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                environmentBlock,
                string.IsNullOrWhiteSpace(request.WorkingDirectory) ? null : request.WorkingDirectory,
                ref startupInfo,
                out processInformation);
            if (!created)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new WindowsPseudoTerminalProcess(
                pseudoConsole,
                inputWrite,
                outputRead,
                processInformation.hProcess,
                processInformation.hThread,
                processInformation.dwProcessId);
        }
        catch
        {
            if (processInformation.hThread != IntPtr.Zero)
            {
                CloseHandle(processInformation.hThread);
            }

            if (processInformation.hProcess != IntPtr.Zero)
            {
                CloseHandle(processInformation.hProcess);
            }

            if (pseudoConsole != IntPtr.Zero)
            {
                ClosePseudoConsoleNative(pseudoConsole);
            }

            inputWrite.Dispose();
            outputRead.Dispose();
            throw;
        }
        finally
        {
            inputRead.Dispose();
            outputWrite.Dispose();
            FreePseudoConsoleAttributeList(attributeList);

            if (environmentBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentBlock);
            }

            if (securityCapabilitiesBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(securityCapabilitiesBuffer);
            }
        }
    }

    private static IntPtr CreateSecurityCapabilitiesBuffer(IntPtr appContainerSid)
    {
        SecurityCapabilities securityCapabilities = new()
        {
            AppContainerSid = appContainerSid
        };
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityCapabilities>());
        Marshal.StructureToPtr(securityCapabilities, buffer, fDeleteOld: false);
        return buffer;
    }

    private static IntPtr CreateWindowsStartupAttributeList(
        IntPtr pseudoConsole,
        IntPtr securityCapabilitiesBuffer)
    {
        List<ProcThreadAttributeUpdate> attributes = [];
        if (pseudoConsole != IntPtr.Zero)
        {
            attributes.Add(new ProcThreadAttributeUpdate(
                ProcThreadAttributePseudoConsole,
                pseudoConsole,
                (IntPtr)IntPtr.Size));
        }

        if (securityCapabilitiesBuffer != IntPtr.Zero)
        {
            attributes.Add(new ProcThreadAttributeUpdate(
                ProcThreadAttributeSecurityCapabilities,
                securityCapabilitiesBuffer,
                (IntPtr)Marshal.SizeOf<SecurityCapabilities>()));
        }

        return CreateProcThreadAttributeList(attributes);
    }

    private static IntPtr CreateProcThreadAttributeList(
        IReadOnlyList<ProcThreadAttributeUpdate> attributes)
    {
        if (attributes.Count == 0)
        {
            return IntPtr.Zero;
        }

        IntPtr size = IntPtr.Zero;
        _ = InitializeProcThreadAttributeList(IntPtr.Zero, attributes.Count, 0, ref size);
        if (size == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        IntPtr attributeList = Marshal.AllocHGlobal(size);
        bool initialized = false;

        try
        {
            if (!InitializeProcThreadAttributeList(attributeList, attributes.Count, 0, ref size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            initialized = true;
            foreach (ProcThreadAttributeUpdate attribute in attributes)
            {
                if (!UpdateProcThreadAttribute(
                        attributeList,
                        0,
                        attribute.Attribute,
                        attribute.Value,
                        attribute.Size,
                        IntPtr.Zero,
                        IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            return attributeList;
        }
        catch
        {
            if (initialized)
            {
                DeleteProcThreadAttributeList(attributeList);
            }

            Marshal.FreeHGlobal(attributeList);
            throw;
        }
    }

    private static void FreePseudoConsoleAttributeList(IntPtr attributeList)
    {
        if (attributeList == IntPtr.Zero)
        {
            return;
        }

        DeleteProcThreadAttributeList(attributeList);
        Marshal.FreeHGlobal(attributeList);
    }

    private static IntPtr CreateWindowsEnvironmentBlock(
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        if (environmentVariables is null || environmentVariables.Count == 0)
        {
            return IntPtr.Zero;
        }

        Dictionary<string, string> mergedEnvironment = new(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                mergedEnvironment[key] = value;
            }
        }

        foreach (KeyValuePair<string, string> environmentVariable in environmentVariables)
        {
            if (!string.IsNullOrWhiteSpace(environmentVariable.Key))
            {
                mergedEnvironment[environmentVariable.Key] = environmentVariable.Value;
            }
        }

        string[] entries = mergedEnvironment
            .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static item => $"{item.Key}={item.Value}")
            .ToArray();
        string block = string.Join('\0', entries) + "\0\0";
        byte[] bytes = Encoding.Unicode.GetBytes(block);
        IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        return buffer;
    }

    private static IntPtr EnsureAppContainerProfile(WindowsSandboxConfiguration sandbox)
    {
        int result = CreateAppContainerProfile(
            sandbox.ProfileName,
            sandbox.ProfileName,
            "NanoAgent shell sandbox",
            IntPtr.Zero,
            0,
            out IntPtr appContainerSid);
        if (result == 0)
        {
            return appContainerSid;
        }

        const int HRESULT_FROM_WIN32_ERROR_ALREADY_EXISTS = unchecked((int)0x800700B7);
        if (result != HRESULT_FROM_WIN32_ERROR_ALREADY_EXISTS)
        {
            throw new Win32Exception(result, $"Unable to create Windows AppContainer profile '{sandbox.ProfileName}'.");
        }

        int deriveResult = DeriveAppContainerSidFromAppContainerName(
            sandbox.ProfileName,
            out appContainerSid);
        if (deriveResult != 0)
        {
            throw new Win32Exception(deriveResult, $"Unable to load Windows AppContainer profile '{sandbox.ProfileName}'.");
        }

        return appContainerSid;
    }

    private static IntPtr PrepareWindowsAppContainer(WindowsSandboxConfiguration sandbox)
    {
        Directory.CreateDirectory(sandbox.TempDirectory);

        IntPtr appContainerSid = EnsureAppContainerProfile(sandbox);
        try
        {
            string appContainerSidText = ConvertSidToString(appContainerSid);
            GrantAppContainerFileAccess(
                appContainerSidText,
                sandbox.WorkspaceRoot,
                sandbox.AllowWorkspaceWrite);
            GrantAppContainerFileAccess(
                appContainerSidText,
                sandbox.TempDirectory,
                allowWrite: true);

            return appContainerSid;
        }
        catch
        {
            if (appContainerSid != IntPtr.Zero)
            {
                FreeSid(appContainerSid);
            }

            throw;
        }
    }

    private static void GrantAppContainerFileAccess(
        string appContainerSid,
        string path,
        bool allowWrite)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        string permission = allowWrite
            ? "(OI)(CI)M"
            : "(OI)(CI)RX";

        ProcessStartInfo startInfo = new()
        {
            FileName = "icacls",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(fullPath);
        startInfo.ArgumentList.Add("/grant:r");
        startInfo.ArgumentList.Add($"*{appContainerSid}:{permission}");
        startInfo.ArgumentList.Add("/T");
        startInfo.ArgumentList.Add("/C");

        using Process process = new()
        {
            StartInfo = startInfo
        };
        process.Start();
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string output = string.IsNullOrWhiteSpace(standardError)
                ? standardOutput
                : standardError;
            throw new Win32Exception(
                process.ExitCode,
                $"Unable to grant Windows AppContainer access to '{fullPath}': {output.Trim()}");
        }
    }

    private static string ConvertSidToString(IntPtr sid)
    {
        if (!ConvertSidToStringSidW(sid, out IntPtr stringSid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            return Marshal.PtrToStringUni(stringSid) ??
                throw new InvalidOperationException("Windows returned an empty AppContainer SID.");
        }
        finally
        {
            LocalFree(stringSid);
        }
    }

    private static void CreateInheritablePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe)
    {
        SecurityAttributes securityAttributes = new()
        {
            nLength = Marshal.SizeOf<SecurityAttributes>(),
            bInheritHandle = true
        };

        if (!CreatePipe(out readPipe, out writePipe, ref securityAttributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static void CreateInputPipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe)
    {
        CreateInheritablePipe(out readPipe, out writePipe);
        try
        {
            ClearHandleInheritance(writePipe);
        }
        catch
        {
            readPipe.Dispose();
            writePipe.Dispose();
            throw;
        }
    }

    private static void CreateOutputPipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe)
    {
        CreateInheritablePipe(out readPipe, out writePipe);
        try
        {
            ClearHandleInheritance(readPipe);
        }
        catch
        {
            readPipe.Dispose();
            writePipe.Dispose();
            throw;
        }
    }

    private static void ClearHandleInheritance(SafeFileHandle handle)
    {
        if (!SetHandleInformation(handle, HandleFlagInherit, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static async Task<int> WaitForWindowsProcessExitAsync(
        WindowsPseudoTerminalProcess process,
        CancellationToken cancellationToken)
    {
        return await WaitForWindowsProcessExitAsync(
            process.ProcessHandle,
            cancellationToken);
    }

    private static async Task<int> WaitForWindowsProcessExitAsync(
        IntPtr processHandle,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            uint waitResult = WaitForSingleObject(processHandle, 50);
            if (waitResult == WaitObject0)
            {
                return GetWindowsProcessExitCode(processHandle);
            }

            if (waitResult == WaitFailed)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (waitResult != WaitTimeout)
            {
                throw new InvalidOperationException(
                    $"Unexpected WaitForSingleObject result '{waitResult}'.");
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private static void TryTerminateWindowsProcess(IntPtr processHandle)
    {
        if (processHandle == IntPtr.Zero)
        {
            return;
        }

        _ = TerminateProcess(processHandle, 1);
    }

    private static void TryKillWindowsProcessTree(
        int processId,
        IntPtr processHandle)
    {
        try
        {
            if (processId > 0)
            {
                Process.GetProcessById(processId).Kill(entireProcessTree: true);
                return;
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }

        TryTerminateWindowsProcess(processHandle);
    }

    private static int GetWindowsProcessExitCode(IntPtr processHandle)
    {
        if (!GetExitCodeProcess(processHandle, out uint exitCode))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return unchecked((int)exitCode);
    }

    private static string BuildPosixCommandLine(ProcessExecutionRequest request)
    {
        return string.Join(
            " ",
            new[] { request.FileName }
                .Concat(request.Arguments)
                .Select(QuotePosixArgument));
    }

    private static string QuotePosixArgument(string value)
    {
        return value.Length == 0
            ? "''"
            : "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static string BuildWindowsCommandLine(ProcessExecutionRequest request)
    {
        return string.Join(
            " ",
            new[] { request.FileName }
                .Concat(request.Arguments)
                .Select(QuoteWindowsArgument));
    }

    private static string QuoteWindowsArgument(string value)
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

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static async Task<string> ReadToEndCappedAsync(
        TextReader reader,
        int? maxCharacters,
        CancellationToken cancellationToken)
    {
        const int BufferSize = 4096;

        if (maxCharacters is <= 0)
        {
            await DrainAsync(reader, cancellationToken);
            return string.Empty;
        }

        char[] buffer = new char[BufferSize];
        StringBuilder builder = maxCharacters is null
            ? new StringBuilder()
            : new StringBuilder(Math.Min(maxCharacters.Value, BufferSize));
        bool truncated = false;

        while (true)
        {
            int read = await reader.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken);
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

            int charactersToAppend = Math.Min(read, remaining);
            builder.Append(buffer, 0, charactersToAppend);
            truncated |= charactersToAppend < read;
        }

        if (truncated && maxCharacters is > 3)
        {
            builder.Length = Math.Min(builder.Length, maxCharacters.Value - 3);
            builder.Append("...");
        }

        return builder.ToString();
    }

    private static async Task DrainAsync(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken) > 0)
        {
        }
    }

    private sealed class SystemBackgroundProcess : IBackgroundProcess
    {
        private readonly Process _process;

        public SystemBackgroundProcess(Process process)
        {
            _process = process;
        }

        public TextReader StandardOutput => _process.StandardOutput;

        public TextReader StandardError => _process.StandardError;

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            TryKillProcess(_process);
            await WaitForExitAsync(cancellationToken);
        }

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _process.WaitForExitAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void Dispose()
        {
            _process.Dispose();
        }
    }

    private sealed class WindowsBackgroundProcess : IBackgroundProcess
    {
        private readonly int _processId;
        private readonly StreamReader _standardError;
        private readonly StreamReader _standardOutput;
        private IntPtr _processHandle;
        private IntPtr _threadHandle;

        public WindowsBackgroundProcess(
            SafeFileHandle stdoutRead,
            SafeFileHandle stderrRead,
            IntPtr processHandle,
            IntPtr threadHandle,
            int processId)
        {
            _standardOutput = CreateReader(stdoutRead);
            _standardError = CreateReader(stderrRead);
            _processHandle = processHandle;
            _threadHandle = threadHandle;
            _processId = processId;
        }

        public TextReader StandardOutput => _standardOutput;

        public TextReader StandardError => _standardError;

        public bool HasExited
        {
            get
            {
                if (_processHandle == IntPtr.Zero)
                {
                    return true;
                }

                uint waitResult = WaitForSingleObject(_processHandle, 0);
                if (waitResult == WaitObject0)
                {
                    return true;
                }

                if (waitResult == WaitTimeout)
                {
                    return false;
                }

                if (waitResult == WaitFailed)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                throw new InvalidOperationException(
                    $"Unexpected WaitForSingleObject result '{waitResult}'.");
            }
        }

        public int ExitCode => _processHandle == IntPtr.Zero
            ? 0
            : GetWindowsProcessExitCode(_processHandle);

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_processHandle != IntPtr.Zero)
            {
                TryKillWindowsProcessTree(
                    _processId,
                    _processHandle);
            }

            await WaitForExitAsync(cancellationToken);
        }

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            if (_processHandle == IntPtr.Zero)
            {
                return;
            }

            await WaitForWindowsProcessExitAsync(
                _processHandle,
                cancellationToken);
        }

        public void Dispose()
        {
            _standardOutput.Dispose();
            _standardError.Dispose();

            if (_threadHandle != IntPtr.Zero)
            {
                CloseHandle(_threadHandle);
                _threadHandle = IntPtr.Zero;
            }

            if (_processHandle != IntPtr.Zero)
            {
                CloseHandle(_processHandle);
                _processHandle = IntPtr.Zero;
            }
        }

        private static StreamReader CreateReader(SafeFileHandle handle)
        {
            FileStream stream = new(
                handle,
                FileAccess.Read,
                bufferSize: 4096,
                isAsync: true);
            return new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false);
        }
    }

    private sealed class WindowsPseudoTerminalProcess : IDisposable
    {
        private IntPtr _pseudoConsole;
        private IntPtr _threadHandle;

        public WindowsPseudoTerminalProcess(
            IntPtr pseudoConsole,
            SafeFileHandle inputWriter,
            SafeFileHandle outputReader,
            IntPtr processHandle,
            IntPtr threadHandle,
            int processId)
        {
            _pseudoConsole = pseudoConsole;
            InputWriter = inputWriter;
            OutputReader = outputReader;
            ProcessHandle = processHandle;
            _threadHandle = threadHandle;
            ProcessId = processId;
        }

        public SafeFileHandle InputWriter { get; }

        public SafeFileHandle OutputReader { get; }

        public IntPtr ProcessHandle { get; private set; }

        private int ProcessId { get; }

        public void ClosePseudoConsole()
        {
            if (_pseudoConsole == IntPtr.Zero)
            {
                return;
            }

            ClosePseudoConsoleNative(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
        }

        public void TryKill()
        {
            TryKillWindowsProcessTree(
                ProcessId,
                ProcessHandle);
        }

        public void Dispose()
        {
            ClosePseudoConsole();
            InputWriter.Dispose();
            OutputReader.Dispose();

            if (_threadHandle != IntPtr.Zero)
            {
                CloseHandle(_threadHandle);
                _threadHandle = IntPtr.Zero;
            }

            if (ProcessHandle != IntPtr.Zero)
            {
                CloseHandle(ProcessHandle);
                ProcessHandle = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public Coord(short x, short y)
        {
            X = x;
            Y = y;
        }

        public short X;

        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;

        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;

        public IntPtr hThread;

        public int dwProcessId;

        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;

        public IntPtr lpSecurityDescriptor;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        public IntPtr AppContainerSid;

        public IntPtr Capabilities;

        public int CapabilityCount;

        public int Reserved;
    }

    private readonly struct ProcThreadAttributeUpdate
    {
        public ProcThreadAttributeUpdate(
            IntPtr attribute,
            IntPtr value,
            IntPtr size)
        {
            Attribute = attribute;
            Value = value;
            Size = size;
        }

        public IntPtr Attribute { get; }

        public IntPtr Value { get; }

        public IntPtr Size { get; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SecurityAttributes lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(
        SafeFileHandle hObject,
        uint dwMask,
        uint dwFlags);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(
        Coord size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsoleNative(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(
        IntPtr hHandle,
        uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(
        IntPtr hProcess,
        out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(
        IntPtr hProcess,
        uint uExitCode);

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int CreateAppContainerProfile(
        string pszAppContainerName,
        string pszDisplayName,
        string pszDescription,
        IntPtr pCapabilities,
        int dwCapabilityCount,
        out IntPtr ppSid);

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeriveAppContainerSidFromAppContainerName(
        string pszAppContainerName,
        out IntPtr ppSid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr FreeSid(IntPtr pSid);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertSidToStringSidW(
        IntPtr Sid,
        out IntPtr StringSid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
