using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Secrets;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class ShellCommandService : IShellCommandService
{
    private const int MaxOutputCharacters = 8_000;
    private const int MaxBackgroundOutputCharacters = 16_000;
    private const string RunningStatus = "running";
    private const string ExitedStatus = "exited";
    private const string FailedStatus = "failed";
    private const string NotFoundStatus = "not_found";
    private const string StoppedStatus = "stopped";

    private readonly ConcurrentDictionary<string, BackgroundTerminal> _backgroundTerminals = new(StringComparer.OrdinalIgnoreCase);
    private readonly IProcessRunner _processRunner;
    private readonly PermissionSettings _permissionSettings;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;
    private int _backgroundTerminalSequence;

    public ShellCommandService(
        IProcessRunner processRunner,
        IWorkspaceRootProvider workspaceRootProvider,
        PermissionSettings? permissionSettings = null)
    {
        _processRunner = processRunner;
        _workspaceRootProvider = workspaceRootProvider;
        _permissionSettings = permissionSettings ?? new PermissionSettings();
    }

    public async Task<ShellCommandExecutionResult> ExecuteAsync(
        ShellCommandExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            throw new ArgumentException(
                "Shell command must be provided.",
                nameof(request));
        }

        PreparedShellCommand prepared = PrepareShellCommand(request);
        if (prepared.SandboxPlan.IsUnsupported)
        {
            return CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                prepared.SandboxPlan.UnsupportedReason!);
        }

        ProcessExecutionResult result;
        try
        {
            result = await _processRunner.RunAsync(
                prepared.ProcessRequest,
                cancellationToken);
        }
        catch (PlatformNotSupportedException exception) when (prepared.ProcessRequest.UsePseudoTerminal)
        {
            return CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                $"Unable to start PTY shell execution: {exception.Message}");
        }
        catch (Win32Exception exception) when (prepared.ProcessRequest.UsePseudoTerminal)
        {
            return CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                $"Unable to start PTY shell execution: {exception.Message}");
        }
        catch (Win32Exception exception) when (IsSandboxRunnerEnforcement(prepared.SandboxPlan.Enforcement))
        {
            return CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                $"Unable to start OS-level shell sandbox runner '{prepared.ProcessRequest.FileName}': {exception.Message}");
        }
        catch (Exception exception) when (
            IsSandboxRunnerEnforcement(prepared.SandboxPlan.Enforcement) &&
            exception is not OperationCanceledException)
        {
            return CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                $"Unable to start OS-level shell sandbox runner '{prepared.ProcessRequest.FileName}': {exception.Message}");
        }
        catch (Win32Exception exception)
        {
            return CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                $"Unable to start shell '{prepared.ProcessRequest.FileName}': {exception.Message}");
        }

        return new ShellCommandExecutionResult(
            request.Command,
            ToWorkspaceRelativePath(prepared.WorkingDirectory),
            result.ExitCode,
            TrimOutput(result.StandardOutput),
            TrimOutput(result.StandardError),
            ShellCommandSandboxArguments.ToWireValue(request.SandboxPermissions),
            string.IsNullOrWhiteSpace(request.Justification)
                ? null
                : request.Justification.Trim(),
            ToWireValue(prepared.EffectiveSandboxMode),
            prepared.SandboxPlan.Enforcement,
            request.PseudoTerminal);
    }

    public Task<ShellCommandExecutionResult> StartBackgroundAsync(
        ShellCommandExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            throw new ArgumentException(
                "Shell command must be provided.",
                nameof(request));
        }

        PreparedShellCommand prepared = PrepareShellCommand(request);
        if (prepared.SandboxPlan.IsUnsupported)
        {
            return Task.FromResult(CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                prepared.SandboxPlan.UnsupportedReason!,
                background: true,
                terminalAction: "start"));
        }

        if (prepared.ProcessRequest.UsePseudoTerminal)
        {
            return Task.FromResult(CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                "Background terminals do not support pseudo-terminal mode.",
                background: true,
                terminalAction: "start"));
        }

        try
        {
            BackgroundTerminal terminal = StartBackgroundTerminal(
                request,
                prepared,
                cancellationToken);
            _backgroundTerminals[terminal.Id] = terminal;

            return Task.FromResult(CreateBackgroundResult(
                terminal,
                terminalAction: "start",
                terminalStatus: terminal.Status,
                exitCode: 0,
                standardOutput: string.Empty,
                standardError: string.Empty));
        }
        catch (Win32Exception exception) when (IsSandboxRunnerEnforcement(prepared.SandboxPlan.Enforcement))
        {
            return Task.FromResult(CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                $"Unable to start OS-level shell sandbox runner '{prepared.ProcessRequest.FileName}': {exception.Message}",
                background: true,
                terminalAction: "start"));
        }
        catch (Exception exception) when (
            IsSandboxRunnerEnforcement(prepared.SandboxPlan.Enforcement) &&
            exception is not OperationCanceledException)
        {
            return Task.FromResult(CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                $"Unable to start OS-level shell sandbox runner '{prepared.ProcessRequest.FileName}': {exception.Message}",
                background: true,
                terminalAction: "start"));
        }
        catch (Win32Exception exception)
        {
            return Task.FromResult(CreateExecutionFailureResult(
                request,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                $"Unable to start shell '{prepared.ProcessRequest.FileName}': {exception.Message}",
                background: true,
                terminalAction: "start"));
        }
    }

    public async Task<ShellCommandExecutionResult> ReadBackgroundAsync(
        string terminalId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetBackgroundTerminal(terminalId, out BackgroundTerminal? terminal))
        {
            return CreateBackgroundNotFoundResult(terminalId, "read");
        }

        BackgroundTerminal activeTerminal = terminal!;
        if (!activeTerminal.IsRunning)
        {
            await activeTerminal.CompleteReadersAsync(cancellationToken);
        }

        (string standardOutput, string standardError) = activeTerminal.ReadNewOutput();
        string status = activeTerminal.Status;
        int exitCode = string.Equals(status, ExitedStatus, StringComparison.Ordinal)
            ? activeTerminal.ExitCodeOrDefault()
            : 0;
        ShellCommandExecutionResult result = CreateBackgroundResult(
            activeTerminal,
            terminalAction: "read",
            terminalStatus: status,
            exitCode,
            standardOutput,
            standardError);

        if (string.Equals(status, ExitedStatus, StringComparison.Ordinal) &&
            _backgroundTerminals.TryRemove(activeTerminal.Id, out BackgroundTerminal? removedTerminal))
        {
            removedTerminal.Dispose();
        }

        return result;
    }

    public async Task<ShellCommandExecutionResult> StopBackgroundAsync(
        string terminalId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_backgroundTerminals.TryRemove(NormalizeTerminalId(terminalId), out BackgroundTerminal? terminal))
        {
            return CreateBackgroundNotFoundResult(terminalId, "stop");
        }

        await terminal.StopAsync(cancellationToken);
        (string standardOutput, string standardError) = terminal.ReadNewOutput();
        ShellCommandExecutionResult result = CreateBackgroundResult(
            terminal,
            terminalAction: "stop",
            terminalStatus: StoppedStatus,
            exitCode: 0,
            standardOutput,
            standardError);
        terminal.Dispose();
        return result;
    }

    private PreparedShellCommand PrepareShellCommand(ShellCommandExecutionRequest request)
    {
        string workingDirectory = ResolveWorkspacePath(request.WorkingDirectory, directoryRequired: true);
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        ToolSandboxMode effectiveSandboxMode = GetEffectiveSandboxMode(request);
        string commandText = OperatingSystem.IsWindows()
            ? BuildWindowsCommandText(request.Command)
            : request.Command;
        ProcessExecutionRequest shellRequest = OperatingSystem.IsWindows()
            ? new ProcessExecutionRequest(
                "powershell",
                ["-NoProfile", "-NonInteractive", "-Command", commandText],
                WorkingDirectory: workingDirectory,
                MaxOutputCharacters: MaxOutputCharacters,
                UsePseudoTerminal: request.PseudoTerminal)
            : new ProcessExecutionRequest(
                "/bin/bash",
                ["-lc", request.Command],
                WorkingDirectory: workingDirectory,
                MaxOutputCharacters: MaxOutputCharacters,
                UsePseudoTerminal: request.PseudoTerminal);

        ShellCommandSandboxPlan sandboxPlan = ShellCommandSandboxPlanner.Create(
            shellRequest,
            effectiveSandboxMode,
            workspaceRoot,
            workingDirectory);
        IReadOnlyDictionary<string, string> sandboxEnvironment = BuildSandboxEnvironment(
            request,
            workspaceRoot,
            effectiveSandboxMode,
            sandboxPlan);
        ProcessExecutionRequest processRequest = sandboxPlan.Request with
        {
            EnvironmentVariables = sandboxEnvironment
        };

        return new PreparedShellCommand(
            workingDirectory,
            effectiveSandboxMode,
            sandboxPlan,
            processRequest);
    }

    private ToolSandboxMode GetEffectiveSandboxMode(ShellCommandExecutionRequest request)
    {
        if (request.SandboxPermissions == ShellCommandSandboxPermissions.RequireEscalated)
        {
            return ToolSandboxMode.DangerFullAccess;
        }

        return _permissionSettings.SandboxMode;
    }

    private ShellCommandExecutionResult CreateExecutionFailureResult(
        ShellCommandExecutionRequest request,
        string workingDirectory,
        string sandboxEnforcement,
        string standardError,
        bool background = false,
        string terminalAction = "run")
    {
        return new ShellCommandExecutionResult(
            request.Command,
            ToWorkspaceRelativePath(workingDirectory),
            126,
            string.Empty,
            TrimOutput(standardError),
            ShellCommandSandboxArguments.ToWireValue(request.SandboxPermissions),
            string.IsNullOrWhiteSpace(request.Justification)
                ? null
                : request.Justification.Trim(),
            ToWireValue(GetEffectiveSandboxMode(request)),
            sandboxEnforcement,
            request.PseudoTerminal,
            background,
            null,
            FailedStatus,
            terminalAction);
    }

    private BackgroundTerminal StartBackgroundTerminal(
        ShellCommandExecutionRequest request,
        PreparedShellCommand prepared,
        CancellationToken cancellationToken)
    {
        IBackgroundProcess process = _processRunner.StartBackground(
            prepared.ProcessRequest,
            cancellationToken);

        string terminalId = "terminal-" + Interlocked.Increment(ref _backgroundTerminalSequence).ToString("D", System.Globalization.CultureInfo.InvariantCulture);
        BackgroundTerminal terminal = new(
            terminalId,
            request.Command,
            ToWorkspaceRelativePath(prepared.WorkingDirectory),
            ShellCommandSandboxArguments.ToWireValue(request.SandboxPermissions),
            string.IsNullOrWhiteSpace(request.Justification)
                ? null
                : request.Justification.Trim(),
            ToWireValue(prepared.EffectiveSandboxMode),
            prepared.SandboxPlan.Enforcement,
            process);
        terminal.StartReaders();
        return terminal;
    }

    private static ShellCommandExecutionResult CreateBackgroundResult(
        BackgroundTerminal terminal,
        string terminalAction,
        string terminalStatus,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        return new ShellCommandExecutionResult(
            terminal.Command,
            terminal.WorkingDirectory,
            exitCode,
            TrimOutput(standardOutput),
            TrimOutput(standardError),
            terminal.SandboxPermissions,
            terminal.Justification,
            terminal.SandboxMode,
            terminal.SandboxEnforcement,
            PseudoTerminal: false,
            Background: true,
            TerminalId: terminal.Id,
            TerminalStatus: terminalStatus,
            TerminalAction: terminalAction);
    }

    private static ShellCommandExecutionResult CreateBackgroundNotFoundResult(
        string terminalId,
        string terminalAction)
    {
        string normalizedTerminalId = NormalizeTerminalId(terminalId);
        return new ShellCommandExecutionResult(
            string.Empty,
            ".",
            127,
            string.Empty,
            $"Background terminal '{normalizedTerminalId}' was not found.",
            Background: true,
            TerminalId: normalizedTerminalId,
            TerminalStatus: NotFoundStatus,
            TerminalAction: terminalAction);
    }

    private bool TryGetBackgroundTerminal(
        string terminalId,
        out BackgroundTerminal? terminal)
    {
        return _backgroundTerminals.TryGetValue(
            NormalizeTerminalId(terminalId),
            out terminal);
    }

    private IReadOnlyDictionary<string, string> BuildSandboxEnvironment(
        ShellCommandExecutionRequest request,
        string workspaceRoot,
        ToolSandboxMode effectiveSandboxMode,
        ShellCommandSandboxPlan sandboxPlan)
    {
        string sandboxEnforcement = sandboxPlan.Enforcement;
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["NANOAGENT_SANDBOX_MODE"] = ToWireValue(_permissionSettings.SandboxMode),
            ["NANOAGENT_SANDBOX_EFFECTIVE_MODE"] = ToWireValue(effectiveSandboxMode),
            ["NANOAGENT_SANDBOX_ENFORCEMENT"] = sandboxEnforcement,
            ["NANOAGENT_SANDBOX_PERMISSIONS"] = ShellCommandSandboxArguments.ToWireValue(request.SandboxPermissions),
            ["NANOAGENT_WORKSPACE_ROOT"] = workspaceRoot
        };

        if (!string.IsNullOrWhiteSpace(request.Justification))
        {
            environment["NANOAGENT_SANDBOX_JUSTIFICATION"] = request.Justification.Trim();
        }

        if (request.PrefixRule is { Count: > 0 })
        {
            environment["NANOAGENT_SANDBOX_PREFIX_RULE"] = string.Join(" ", request.PrefixRule);
        }

        if (request.PseudoTerminal)
        {
            environment["NANOAGENT_SHELL_PTY"] = "1";
        }

        bool usesWindowsAppContainer = string.Equals(
            sandboxEnforcement,
            ShellCommandSandboxPlanner.WindowsAppContainerEnforcement,
            StringComparison.Ordinal);

        if (IsSandboxRunnerEnforcement(sandboxEnforcement) && !usesWindowsAppContainer)
        {
            environment["HOME"] = workspaceRoot;
            environment["TMPDIR"] = "/tmp";
        }

        if (usesWindowsAppContainer)
        {
            string tempDirectory = sandboxPlan.Request.WindowsSandbox?.TempDirectory ??
                Path.Combine(workspaceRoot, ".nanoagent", "sandbox-temp");
            environment["HOME"] = workspaceRoot;
            environment["USERPROFILE"] = workspaceRoot;
            environment["TEMP"] = tempDirectory;
            environment["TMP"] = tempDirectory;
        }

        return environment;
    }

    private static string ToWireValue(ToolSandboxMode sandboxMode)
    {
        return sandboxMode switch
        {
            ToolSandboxMode.ReadOnly => "read-only",
            ToolSandboxMode.DangerFullAccess => "danger-full-access",
            _ => "workspace-write"
        };
    }

    private static string BuildWindowsCommandText(string commandText)
    {
        IReadOnlyList<ShellCommandSegment> segments = ShellCommandText.ParseSegments(commandText);
        if (segments.Count <= 1)
        {
            return commandText;
        }

        StringBuilder scriptBuilder = new();
        scriptBuilder.AppendLine("$ErrorActionPreference = 'Continue'");
        scriptBuilder.AppendLine("$__nano_exit = 0");
        scriptBuilder.AppendLine("$__nano_segment_exit = 0");
        scriptBuilder.AppendLine("function Invoke-NanoSegment([string]$encoded) {");
        scriptBuilder.AppendLine("  Set-Variable -Name LASTEXITCODE -Scope Global -Value 0 -Force");
        scriptBuilder.AppendLine("  $script:__nano_segment_exit = 0");
        scriptBuilder.AppendLine("  $scriptText = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($encoded))");
        scriptBuilder.AppendLine("  try {");
        scriptBuilder.AppendLine("    & ([ScriptBlock]::Create($scriptText))");
        scriptBuilder.AppendLine("    if (-not $?) { $script:__nano_segment_exit = 1; return }");
        scriptBuilder.AppendLine("    $script:__nano_segment_exit = [int]$global:LASTEXITCODE");
        scriptBuilder.AppendLine("  }");
        scriptBuilder.AppendLine("  catch {");
        scriptBuilder.AppendLine("    Write-Error $_");
        scriptBuilder.AppendLine("    $script:__nano_segment_exit = 1");
        scriptBuilder.AppendLine("  }");
        scriptBuilder.AppendLine("}");

        for (int index = 0; index < segments.Count; index++)
        {
            ShellCommandSegment segment = segments[index];
            string encodedSegment = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(segment.CommandText));
            string invocation = $"Invoke-NanoSegment('{encodedSegment}'); $__nano_exit = $__nano_segment_exit";

            if (index == 0 || segment.Condition == ShellCommandSegmentCondition.Always)
            {
                scriptBuilder.AppendLine(invocation);
                continue;
            }

            if (segment.Condition == ShellCommandSegmentCondition.OnSuccess)
            {
                scriptBuilder.AppendLine($"if ($__nano_exit -eq 0) {{ {invocation} }}");
                continue;
            }

            scriptBuilder.AppendLine($"if ($__nano_exit -ne 0) {{ {invocation} }}");
        }

        scriptBuilder.AppendLine("exit $__nano_exit");
        return scriptBuilder.ToString();
    }

    private string ResolveWorkspacePath(
        string? requestedPath,
        bool directoryRequired)
    {
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        string fullPath = WorkspacePath.Resolve(workspaceRoot, requestedPath);

        if (directoryRequired && !Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Directory '{ToWorkspaceRelativePath(fullPath)}' does not exist.");
        }

        return fullPath;
    }

    private string ToWorkspaceRelativePath(string fullPath)
    {
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        return WorkspacePath.ToRelativePath(workspaceRoot, fullPath);
    }

    private static string TrimOutput(string value)
    {
        string normalizedValue = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalizedValue.Length <= MaxOutputCharacters)
        {
            return normalizedValue;
        }

        return normalizedValue[..MaxOutputCharacters] + "...";
    }

    private static string NormalizeTerminalId(string? terminalId)
    {
        return string.IsNullOrWhiteSpace(terminalId)
            ? string.Empty
            : terminalId.Trim();
    }

    private static bool IsSandboxRunnerEnforcement(string enforcement)
    {
        return string.Equals(
                   enforcement,
                   ShellCommandSandboxPlanner.BubblewrapEnforcement,
                   StringComparison.Ordinal) ||
               string.Equals(
                   enforcement,
                   ShellCommandSandboxPlanner.SandboxExecEnforcement,
                   StringComparison.Ordinal) ||
               string.Equals(
                   enforcement,
                   ShellCommandSandboxPlanner.WindowsAppContainerEnforcement,
                   StringComparison.Ordinal);
    }

    private sealed record PreparedShellCommand(
        string WorkingDirectory,
        ToolSandboxMode EffectiveSandboxMode,
        ShellCommandSandboxPlan SandboxPlan,
        ProcessExecutionRequest ProcessRequest);

    private sealed class BackgroundTerminal : IDisposable
    {
        private readonly StringBuilder _standardError = new();
        private readonly StringBuilder _standardOutput = new();
        private readonly object _syncRoot = new();
        private bool _stopped;
        private Task _standardErrorTask = Task.CompletedTask;
        private int _standardErrorCursor;
        private Task _standardOutputTask = Task.CompletedTask;
        private int _standardOutputCursor;

        public BackgroundTerminal(
            string id,
            string command,
            string workingDirectory,
            string sandboxPermissions,
            string? justification,
            string sandboxMode,
            string sandboxEnforcement,
            IBackgroundProcess process)
        {
            Id = id;
            Command = command;
            WorkingDirectory = workingDirectory;
            SandboxPermissions = sandboxPermissions;
            Justification = justification;
            SandboxMode = sandboxMode;
            SandboxEnforcement = sandboxEnforcement;
            Process = process;
        }

        public string Command { get; }

        public string Id { get; }

        public bool IsRunning => !_stopped && !Process.HasExited;

        public string? Justification { get; }

        public IBackgroundProcess Process { get; }

        public string SandboxEnforcement { get; }

        public string SandboxMode { get; }

        public string SandboxPermissions { get; }

        public string Status
        {
            get
            {
                if (_stopped)
                {
                    return StoppedStatus;
                }

                return Process.HasExited
                    ? ExitedStatus
                    : RunningStatus;
            }
        }

        public string WorkingDirectory { get; }

        public void StartReaders()
        {
            _standardOutputTask = ReadStreamAsync(
                Process.StandardOutput,
                AppendStandardOutput);
            _standardErrorTask = ReadStreamAsync(
                Process.StandardError,
                AppendStandardError);
        }

        public (string StandardOutput, string StandardError) ReadNewOutput()
        {
            lock (_syncRoot)
            {
                string standardOutput = _standardOutputCursor >= _standardOutput.Length
                    ? string.Empty
                    : _standardOutput.ToString(_standardOutputCursor, _standardOutput.Length - _standardOutputCursor);
                string standardError = _standardErrorCursor >= _standardError.Length
                    ? string.Empty
                    : _standardError.ToString(_standardErrorCursor, _standardError.Length - _standardErrorCursor);
                _standardOutputCursor = _standardOutput.Length;
                _standardErrorCursor = _standardError.Length;
                return (standardOutput, standardError);
            }
        }

        public int ExitCodeOrDefault()
        {
            try
            {
                return Process.HasExited
                    ? Process.ExitCode
                    : 0;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
            catch (Win32Exception)
            {
                return 0;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _stopped = true;
            await Process.StopAsync(cancellationToken);
            await CompleteReadersAsync(cancellationToken);
        }

        public async Task CompleteReadersAsync(CancellationToken cancellationToken)
        {
            await Task.WhenAll(_standardOutputTask, _standardErrorTask).WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            Process.Dispose();
        }

        private void AppendStandardOutput(string value)
        {
            Append(_standardOutput, ref _standardOutputCursor, value);
        }

        private void AppendStandardError(string value)
        {
            Append(_standardError, ref _standardErrorCursor, value);
        }

        private void Append(
            StringBuilder builder,
            ref int cursor,
            string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            lock (_syncRoot)
            {
                builder.Append(value);
                if (builder.Length <= MaxBackgroundOutputCharacters)
                {
                    return;
                }

                int overflow = builder.Length - MaxBackgroundOutputCharacters;
                builder.Remove(0, overflow);
                cursor = Math.Max(0, cursor - overflow);
            }
        }

        private static async Task ReadStreamAsync(
            TextReader reader,
            Action<string> append)
        {
            char[] buffer = new char[4096];
            while (true)
            {
                int read;
                try
                {
                    read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length));
                }
                catch (IOException exception)
                {
                    append($"{Environment.NewLine}Output capture stopped: {exception.Message}");
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (read == 0)
                {
                    return;
                }

                append(new string(buffer, 0, read));
            }
        }
    }
}
