using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Infrastructure.WindowsSandbox;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class ShellCommandService : IShellCommandService, IDisposable
{
    private const int MaxOutputCharacters = 8_000;
    private const int MaxBackgroundOutputCharacters = 16_000;
    private const int DefaultCompletedBackgroundTerminalTtlSeconds = 300;
    private const int DefaultMaxConcurrentBackgroundTerminalsPerSession = 4;
    private const string RunningStatus = "running";
    private const string ExitedStatus = "exited";
    private const string FailedStatus = "failed";
    private const string NotFoundStatus = "not_found";
    private const string StoppedStatus = "stopped";

    private readonly ConcurrentDictionary<string, BackgroundTerminal> _backgroundTerminals = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _backgroundTerminalGate = new();
    private readonly TimeSpan _completedBackgroundTerminalTtl;
    private readonly EventHandler _processExitHandler;
    private readonly int _maxConcurrentBackgroundTerminalsPerSession;
    private readonly IProcessRunner _processRunner;
    private readonly PermissionSettings _permissionSettings;
    private readonly TimeProvider _timeProvider;
    private readonly IWindowsSandboxProcessRunner? _windowsSandboxProcessRunner;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;
    private int _backgroundTerminalSequence;
    private bool _disposed;

    public ShellCommandService(
        IProcessRunner processRunner,
        IWorkspaceRootProvider workspaceRootProvider,
        PermissionSettings? permissionSettings = null,
        ToolExecutionSettings? toolExecutionSettings = null,
        TimeProvider? timeProvider = null,
        IWindowsSandboxProcessRunner? windowsSandboxProcessRunner = null)
    {
        _processRunner = processRunner;
        _workspaceRootProvider = workspaceRootProvider;
        _permissionSettings = permissionSettings ?? new PermissionSettings();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _windowsSandboxProcessRunner = windowsSandboxProcessRunner;
        _maxConcurrentBackgroundTerminalsPerSession = Math.Max(
            1,
            toolExecutionSettings?.MaxConcurrentBackgroundTerminalsPerSession ??
            DefaultMaxConcurrentBackgroundTerminalsPerSession);
        _completedBackgroundTerminalTtl = TimeSpan.FromSeconds(Math.Max(
            1,
            toolExecutionSettings?.CompletedBackgroundTerminalTtlSeconds ??
            DefaultCompletedBackgroundTerminalTtlSeconds));
        _processExitHandler = (_, _) => StopAllBackgroundTerminals();
        AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
    }

    public async Task<ShellCommandExecutionResult> ExecuteAsync(
        ShellCommandExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        ShellCommandExecutionRequest normalizedRequest = NormalizeRequest(request);

        if (string.IsNullOrWhiteSpace(normalizedRequest.Command))
        {
            throw new ArgumentException(
                "Shell command must be provided.",
                nameof(request));
        }

        PreparedShellCommand prepared = PrepareShellCommand(normalizedRequest);

        ProcessExecutionResult result;
        if (IsWindowsSandboxEnforcement(prepared.SandboxPlan.Enforcement))
        {
            if (prepared.ProcessRequest.UsePseudoTerminal)
            {
                return CreateExecutionFailureResult(
                    normalizedRequest,
                    prepared.WorkingDirectory,
                    prepared.SandboxPlan.Enforcement,
                    "Pseudo-terminal mode is not supported when Windows OS sandboxing is active. Re-run without 'pty' or request sandbox escalation.");
            }

            if (_windowsSandboxProcessRunner is null)
            {
                return CreateExecutionFailureResult(
                    normalizedRequest,
                    prepared.WorkingDirectory,
                    prepared.SandboxPlan.Enforcement,
                    "Windows OS sandboxing is not configured for shell execution.");
            }

            try
            {
                result = await _windowsSandboxProcessRunner.RunAsync(
                    prepared.ProcessRequest,
                    CreateWindowsSandboxExecutionContext(
                        prepared.WorkingDirectory,
                        prepared.EffectiveSandboxMode),
                    cancellationToken);
            }
            catch (PlatformNotSupportedException exception)
            {
                return CreateExecutionFailureResult(
                    normalizedRequest,
                    prepared.WorkingDirectory,
                    prepared.SandboxPlan.Enforcement,
                    $"Unable to start Windows OS sandbox shell execution: {exception.Message}");
            }
            catch (Win32Exception exception)
            {
                return CreateExecutionFailureResult(
                    normalizedRequest,
                    prepared.WorkingDirectory,
                    prepared.SandboxPlan.Enforcement,
                    $"Unable to start Windows OS sandbox shell execution: {exception.Message}");
            }
        }
        else
        {
            try
            {
                result = await _processRunner.RunAsync(
                    prepared.ProcessRequest,
                    cancellationToken);
            }
            catch (PlatformNotSupportedException exception) when (prepared.ProcessRequest.UsePseudoTerminal)
            {
                return CreateExecutionFailureResult(
                    normalizedRequest,
                    prepared.WorkingDirectory,
                    prepared.SandboxPlan.Enforcement,
                    $"Unable to start PTY shell execution: {exception.Message}");
            }
            catch (Win32Exception exception) when (prepared.ProcessRequest.UsePseudoTerminal)
            {
                return CreateExecutionFailureResult(
                    normalizedRequest,
                    prepared.WorkingDirectory,
                    prepared.SandboxPlan.Enforcement,
                    $"Unable to start PTY shell execution: {exception.Message}");
            }
            catch (Win32Exception exception) when (IsSandboxRunnerEnforcement(prepared.SandboxPlan.Enforcement))
            {
                return CreateExecutionFailureResult(
                    normalizedRequest,
                    prepared.WorkingDirectory,
                    prepared.SandboxPlan.Enforcement,
                    $"Unable to start OS-level shell sandbox runner '{prepared.ProcessRequest.FileName}': {exception.Message}");
            }
            catch (Win32Exception exception)
            {
                return CreateExecutionFailureResult(
                    normalizedRequest,
                    prepared.WorkingDirectory,
                    prepared.SandboxPlan.Enforcement,
                    $"Unable to start shell '{prepared.ProcessRequest.FileName}': {exception.Message}");
            }
        }

        return new ShellCommandExecutionResult(
            normalizedRequest.Command,
            ToWorkspaceRelativePath(prepared.WorkingDirectory),
            result.ExitCode,
            TrimOutput(result.StandardOutput),
            TrimOutput(result.StandardError),
            ShellCommandSandboxArguments.ToWireValue(normalizedRequest.SandboxPermissions),
            string.IsNullOrWhiteSpace(normalizedRequest.Justification)
                ? null
                : normalizedRequest.Justification.Trim(),
            ToWireValue(prepared.EffectiveSandboxMode),
            prepared.SandboxPlan.Enforcement,
            normalizedRequest.PseudoTerminal);
    }

    public Task<ShellCommandExecutionResult> StartBackgroundAsync(
        ShellCommandExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        ShellCommandExecutionRequest normalizedRequest = NormalizeRequest(request);

        if (string.IsNullOrWhiteSpace(normalizedRequest.Command))
        {
            throw new ArgumentException(
                "Shell command must be provided.",
                nameof(request));
        }

        PreparedShellCommand prepared = PrepareShellCommand(normalizedRequest);
        if (IsWindowsSandboxEnforcement(prepared.SandboxPlan.Enforcement))
        {
            return Task.FromResult(CreateExecutionFailureResult(
                normalizedRequest,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                "Background terminals are not supported when Windows OS sandboxing is active. Re-run in the foreground or request sandbox escalation.",
                background: true,
                terminalAction: "start"));
        }

        if (prepared.ProcessRequest.UsePseudoTerminal)
        {
            return Task.FromResult(CreateExecutionFailureResult(
                normalizedRequest,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                "Background terminals do not support pseudo-terminal mode.",
                background: true,
                terminalAction: "start"));
        }

        try
        {
            lock (_backgroundTerminalGate)
            {
                RemoveExpiredCompletedTerminals();
                string sessionId = NormalizeSessionId(normalizedRequest.SessionId);
                int runningCount = _backgroundTerminals.Values.Count(terminal =>
                    string.Equals(terminal.SessionId, sessionId, StringComparison.Ordinal) &&
                    terminal.IsRunning);

                if (runningCount >= _maxConcurrentBackgroundTerminalsPerSession)
                {
                    return Task.FromResult(CreateExecutionFailureResult(
                        normalizedRequest,
                        prepared.WorkingDirectory,
                        prepared.SandboxPlan.Enforcement,
                        $"Maximum background terminals per session reached ({_maxConcurrentBackgroundTerminalsPerSession}). Stop one with /terminals stop <id> before starting another.",
                        background: true,
                        terminalAction: "start"));
                }

                BackgroundTerminal terminal = StartBackgroundTerminal(
                    normalizedRequest,
                    prepared);
                _backgroundTerminals[terminal.Id] = terminal;

                return Task.FromResult(CreateBackgroundResult(
                    terminal,
                    terminalAction: "start",
                    terminalStatus: terminal.Status,
                    exitCode: 0,
                    standardOutput: string.Empty,
                    standardError: string.Empty));
            }
        }
        catch (Win32Exception exception) when (IsSandboxRunnerEnforcement(prepared.SandboxPlan.Enforcement))
        {
            return Task.FromResult(CreateExecutionFailureResult(
                normalizedRequest,
                prepared.WorkingDirectory,
                prepared.SandboxPlan.Enforcement,
                $"Unable to start OS-level shell sandbox runner '{prepared.ProcessRequest.FileName}': {exception.Message}",
                background: true,
                terminalAction: "start"));
        }
        catch (Win32Exception exception)
        {
            return Task.FromResult(CreateExecutionFailureResult(
                normalizedRequest,
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
        ThrowIfDisposed();
        RemoveExpiredCompletedTerminals();

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

        return result;
    }

    public async Task<ShellCommandExecutionResult> StopBackgroundAsync(
        string terminalId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        RemoveExpiredCompletedTerminals();

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

    public Task<IReadOnlyList<BackgroundTerminalInfo>> ListBackgroundAsync(
        string? sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        RemoveExpiredCompletedTerminals();

        string normalizedSessionId = NormalizeSessionId(sessionId);
        IReadOnlyList<BackgroundTerminalInfo> terminals = _backgroundTerminals.Values
            .Where(terminal => string.Equals(terminal.SessionId, normalizedSessionId, StringComparison.Ordinal))
            .Select(CreateBackgroundTerminalInfo)
            .OrderBy(static terminal => terminal.StartedAtUtc)
            .ThenBy(static terminal => terminal.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(terminals);
    }

    private static ShellCommandExecutionRequest NormalizeRequest(ShellCommandExecutionRequest request)
    {
        string normalizedCommand = ShellCommandText.NormalizeCommandText(request.Command).Trim();
        return string.Equals(normalizedCommand, request.Command, StringComparison.Ordinal)
            ? request
            : request with { Command = normalizedCommand };
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
            sandboxPlan.Enforcement);
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

    private WindowsSandboxExecutionContext CreateWindowsSandboxExecutionContext(
        string workingDirectory,
        ToolSandboxMode effectiveSandboxMode)
    {
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        IReadOnlyList<string> writableRoots = effectiveSandboxMode == ToolSandboxMode.WorkspaceWrite
            ? [workspaceRoot]
            : [];

        return new WindowsSandboxExecutionContext(
            effectiveSandboxMode,
            WindowsSandboxPaths.ResolveAppHome(),
            workspaceRoot,
            workingDirectory,
            writableRoots,
            IncludeTempEnvironmentVariables: effectiveSandboxMode == ToolSandboxMode.WorkspaceWrite);
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
        PreparedShellCommand prepared)
    {
        ProcessStartInfo startInfo = CreateBackgroundStartInfo(prepared.ProcessRequest);
        Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        string terminalId = "terminal-" + Interlocked.Increment(ref _backgroundTerminalSequence).ToString("D", System.Globalization.CultureInfo.InvariantCulture);
        BackgroundTerminal terminal = new(
            terminalId,
            NormalizeSessionId(request.SessionId),
            request.Command,
            ToWorkspaceRelativePath(prepared.WorkingDirectory),
            ShellCommandSandboxArguments.ToWireValue(request.SandboxPermissions),
            string.IsNullOrWhiteSpace(request.Justification)
                ? null
                : request.Justification.Trim(),
            ToWireValue(prepared.EffectiveSandboxMode),
            prepared.SandboxPlan.Enforcement,
            process,
            _timeProvider);
        try
        {
            process.Start();
        }
        catch
        {
            terminal.Dispose();
            throw;
        }

        terminal.StartReaders();
        return terminal;
    }

    private static ProcessStartInfo CreateBackgroundStartInfo(ProcessExecutionRequest request)
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

        return startInfo;
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

    private BackgroundTerminalInfo CreateBackgroundTerminalInfo(BackgroundTerminal terminal)
    {
        string status = terminal.Status;
        int? exitCode = string.Equals(status, ExitedStatus, StringComparison.Ordinal)
            ? terminal.ExitCodeOrDefault()
            : null;
        DateTimeOffset? completedAtUtc = terminal.CompletedAtUtc;
        DateTimeOffset? expiresAtUtc = string.Equals(status, ExitedStatus, StringComparison.Ordinal) &&
                                       completedAtUtc is not null
            ? completedAtUtc.Value + _completedBackgroundTerminalTtl
            : null;

        return new BackgroundTerminalInfo(
            terminal.Id,
            terminal.SessionId,
            terminal.Command,
            terminal.WorkingDirectory,
            status,
            exitCode,
            terminal.StartedAtUtc,
            completedAtUtc,
            expiresAtUtc);
    }

    private bool TryGetBackgroundTerminal(
        string terminalId,
        out BackgroundTerminal? terminal)
    {
        return _backgroundTerminals.TryGetValue(
            NormalizeTerminalId(terminalId),
            out terminal);
    }

    private void RemoveExpiredCompletedTerminals()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (BackgroundTerminal terminal in _backgroundTerminals.Values)
        {
            if (!terminal.IsExpired(now, _completedBackgroundTerminalTtl))
            {
                continue;
            }

            if (_backgroundTerminals.TryRemove(terminal.Id, out BackgroundTerminal? removedTerminal))
            {
                removedTerminal.Dispose();
            }
        }
    }

    private void StopAllBackgroundTerminals()
    {
        foreach (KeyValuePair<string, BackgroundTerminal> item in _backgroundTerminals.ToArray())
        {
            if (!_backgroundTerminals.TryRemove(item.Key, out BackgroundTerminal? terminal))
            {
                continue;
            }

            try
            {
                terminal.Stop();
            }
            catch
            {
            }
            finally
            {
                terminal.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
        StopAllBackgroundTerminals();
    }

    private IReadOnlyDictionary<string, string> BuildSandboxEnvironment(
        ShellCommandExecutionRequest request,
        string workspaceRoot,
        ToolSandboxMode effectiveSandboxMode,
        string sandboxEnforcement)
    {
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

    private static string NormalizeSessionId(string? sessionId)
    {
        return string.IsNullOrWhiteSpace(sessionId)
            ? string.Empty
            : sessionId.Trim();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
                   ShellCommandSandboxPlanner.WindowsSandboxEnforcement,
                   StringComparison.Ordinal);
    }

    private static bool IsWindowsSandboxEnforcement(string enforcement)
    {
        return string.Equals(
            enforcement,
            ShellCommandSandboxPlanner.WindowsSandboxEnforcement,
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
        private readonly TimeProvider _timeProvider;
        private DateTimeOffset? _completedAtUtc;
        private bool _stopped;
        private Task _standardErrorTask = Task.CompletedTask;
        private int _standardErrorCursor;
        private Task _standardOutputTask = Task.CompletedTask;
        private int _standardOutputCursor;

        public BackgroundTerminal(
            string id,
            string sessionId,
            string command,
            string workingDirectory,
            string sandboxPermissions,
            string? justification,
            string sandboxMode,
            string sandboxEnforcement,
            Process process,
            TimeProvider timeProvider)
        {
            Id = id;
            SessionId = sessionId;
            Command = command;
            WorkingDirectory = workingDirectory;
            SandboxPermissions = sandboxPermissions;
            Justification = justification;
            SandboxMode = sandboxMode;
            SandboxEnforcement = sandboxEnforcement;
            Process = process;
            _timeProvider = timeProvider;
            StartedAtUtc = timeProvider.GetUtcNow();
            process.Exited += (_, _) => MarkCompleted();
        }

        public string Command { get; }

        public DateTimeOffset? CompletedAtUtc
        {
            get
            {
                _ = Status;
                return _completedAtUtc;
            }
        }

        public string Id { get; }

        public bool IsRunning => string.Equals(Status, RunningStatus, StringComparison.Ordinal);

        public string? Justification { get; }

        public Process Process { get; }

        public string SandboxEnforcement { get; }

        public string SandboxMode { get; }

        public string SandboxPermissions { get; }

        public string SessionId { get; }

        public DateTimeOffset StartedAtUtc { get; }

        public string Status
        {
            get
            {
                if (_stopped)
                {
                    return StoppedStatus;
                }

                if (HasExited())
                {
                    MarkCompleted();
                    return ExitedStatus;
                }

                return RunningStatus;
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
                return HasExited()
                    ? Process.ExitCode
                    : 0;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _stopped = true;
            KillIfRunning();
            await WaitForExitAsync(cancellationToken);
            await CompleteReadersAsync(cancellationToken);
        }

        public void Stop()
        {
            _stopped = true;
            KillIfRunning();
            try
            {
                Process.WaitForExit(milliseconds: 2_000);
            }
            catch (InvalidOperationException)
            {
            }
        }

        public async Task CompleteReadersAsync(CancellationToken cancellationToken)
        {
            await Task.WhenAll(_standardOutputTask, _standardErrorTask).WaitAsync(cancellationToken);
        }

        public bool IsExpired(
            DateTimeOffset now,
            TimeSpan ttl)
        {
            if (string.Equals(Status, RunningStatus, StringComparison.Ordinal) ||
                _completedAtUtc is null)
            {
                return false;
            }

            return now - _completedAtUtc.Value >= ttl;
        }

        public void Dispose()
        {
            Process.Dispose();
        }

        private async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Process.WaitForExitAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private bool HasExited()
        {
            try
            {
                return Process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private void KillIfRunning()
        {
            try
            {
                if (!HasExited())
                {
                    Process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        private void MarkCompleted()
        {
            if (_completedAtUtc is not null)
            {
                return;
            }

            lock (_syncRoot)
            {
                _completedAtUtc ??= _timeProvider.GetUtcNow();
            }
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
