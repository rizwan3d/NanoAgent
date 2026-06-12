using NanoAgent.Infrastructure.Secrets;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Text;

namespace NanoAgent.Infrastructure.WindowsSandbox;

/// <summary>
/// <see cref="IBackgroundProcessHandle"/> for a Windows OS-sandboxed background command.
/// The sandbox runner helper process (launched as the restricted sandbox user) streams the child's
/// stdout/stderr over the outbound IPC pipe; this handle pumps those messages into the owning
/// terminal's buffers. Termination is requested by sending a <see cref="WindowsSandboxIpcMessageKind.Terminate"/>
/// message; the helper process is force-terminated as a fallback. The temporary ACLs prepared for the
/// command are revoked when the handle is disposed, so they live for the full lifetime of the command.
/// </summary>
internal sealed class WindowsSandboxBackgroundProcess : IBackgroundProcessHandle
{
    private static readonly TimeSpan TerminateGracePeriod = TimeSpan.FromSeconds(3);

    private readonly NamedPipeServerStream _inbound;
    private readonly NamedPipeServerStream _outbound;
    private readonly string _nanoAgentHome;
    private readonly IReadOnlyList<(string Path, string Sid, FileSystemRights Rights, AccessControlType Type)> _temporaryAces;
    private readonly CancellationTokenSource _pumpCts = new();
    private readonly TaskCompletionSource _exitSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _inboundWriteLock = new(1, 1);
    private readonly object _stateGate = new();

    private WindowsSandboxNative.ProcessInformation _processInformation;
    private bool _processHandlesClosed;
    private Task _pumpTask = Task.CompletedTask;
    private int _exitCode;
    private volatile bool _exited;
    private bool _killRequested;
    private bool _disposed;

    public WindowsSandboxBackgroundProcess(
        WindowsSandboxNative.ProcessInformation processInformation,
        NamedPipeServerStream inbound,
        NamedPipeServerStream outbound,
        string nanoAgentHome,
        IReadOnlyList<(string Path, string Sid, FileSystemRights Rights, AccessControlType Type)> temporaryAces)
    {
        _processInformation = processInformation;
        _inbound = inbound;
        _outbound = outbound;
        _nanoAgentHome = nanoAgentHome;
        _temporaryAces = temporaryAces;
    }

    public event EventHandler? Exited;

    public bool HasExited => _exited;

    public int ExitCode => _exitCode;

    public void StartStreaming(
        Action<string> onStandardOutput,
        Action<string> onStandardError)
    {
        _pumpTask = PumpAsync(onStandardOutput, onStandardError);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return _exitSignal.Task.WaitAsync(cancellationToken);
    }

    public bool WaitForExit(int milliseconds)
    {
        try
        {
            return _exitSignal.Task.Wait(milliseconds);
        }
        catch (AggregateException)
        {
            return _exited;
        }
    }

    public async Task CompleteStreamingAsync(CancellationToken cancellationToken)
    {
        await _pumpTask.WaitAsync(cancellationToken);
    }

    public void Kill()
    {
        lock (_stateGate)
        {
            if (_killRequested)
            {
                return;
            }

            _killRequested = true;
        }

        _ = KillCoreAsync();
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _pumpCts.Cancel();
        TerminateHelperProcess();
        CloseProcessHandles();

        try
        {
            _inbound.Dispose();
        }
        catch
        {
        }

        try
        {
            _outbound.Dispose();
        }
        catch
        {
        }

        RevokeTemporaryAces();

        _pumpCts.Dispose();
        _inboundWriteLock.Dispose();
    }

    private async Task KillCoreAsync()
    {
        try
        {
            await SendTerminateAsync();
        }
        catch (Exception exception)
        {
            WindowsSandboxLog.Write(_nanoAgentHome, $"background terminate send failed: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            await _exitSignal.Task.WaitAsync(TerminateGracePeriod);
        }
        catch (TimeoutException)
        {
            WindowsSandboxLog.Write(_nanoAgentHome, "background terminate grace expired; force-terminating runner");
        }

        if (!_exited)
        {
            TerminateHelperProcess();
        }
    }

    private async Task SendTerminateAsync()
    {
        await _inboundWriteLock.WaitAsync();
        try
        {
            await WindowsSandboxFramedIpc.WriteAsync(
                _inbound,
                new WindowsSandboxIpcMessage
                {
                    Kind = WindowsSandboxIpcMessageKind.Terminate
                },
                CancellationToken.None);
        }
        finally
        {
            _inboundWriteLock.Release();
        }
    }

    private async Task PumpAsync(
        Action<string> onStandardOutput,
        Action<string> onStandardError)
    {
        try
        {
            while (true)
            {
                WindowsSandboxIpcMessage message = await WindowsSandboxFramedIpc.ReadAsync(_outbound, _pumpCts.Token);
                switch (message.Kind)
                {
                    case WindowsSandboxIpcMessageKind.Output:
                        string text = DecodeOutput(message.PayloadBase64);
                        if (text.Length == 0)
                        {
                            break;
                        }

                        if (message.Stream == WindowsSandboxOutputStream.Stderr)
                        {
                            onStandardError(text);
                        }
                        else
                        {
                            onStandardOutput(text);
                        }

                        break;

                    case WindowsSandboxIpcMessageKind.Exit:
                        Complete(message.ExitCode ?? 0);
                        return;

                    case WindowsSandboxIpcMessageKind.Error:
                        if (!string.IsNullOrWhiteSpace(message.Error))
                        {
                            onStandardError(message.Error!);
                        }

                        Complete(message.ExitCode ?? 126);
                        return;
                }
            }
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
        {
            // The helper closed the pipe (or we are disposing). Treat as termination.
            Complete(_exited ? _exitCode : 137);
        }
    }

    private void Complete(int exitCode)
    {
        lock (_stateGate)
        {
            if (_exited)
            {
                return;
            }

            _exitCode = exitCode;
            _exited = true;
        }

        _exitSignal.TrySetResult();
        Exited?.Invoke(this, EventArgs.Empty);
    }

    private void TerminateHelperProcess()
    {
        lock (_stateGate)
        {
            if (_processHandlesClosed || _processInformation.hProcess == IntPtr.Zero)
            {
                return;
            }

            try
            {
                WindowsSandboxNative.TerminateProcess(_processInformation.hProcess, 1);
            }
            catch
            {
            }
        }
    }

    private void CloseProcessHandles()
    {
        lock (_stateGate)
        {
            if (_processHandlesClosed)
            {
                return;
            }

            _processHandlesClosed = true;
            if (_processInformation.hThread != IntPtr.Zero)
            {
                WindowsSandboxNative.CloseHandle(_processInformation.hThread);
            }

            if (_processInformation.hProcess != IntPtr.Zero)
            {
                WindowsSandboxNative.CloseHandle(_processInformation.hProcess);
            }

            _processInformation = default;
        }
    }

    private void RevokeTemporaryAces()
    {
        foreach ((string path, string sid, FileSystemRights rights, AccessControlType type) in _temporaryAces)
        {
            try
            {
                WindowsSandboxAcl.RevokeAce(path, sid, rights, type);
            }
            catch (Exception exception)
            {
                WindowsSandboxLog.Write(_nanoAgentHome, $"background cleanup failure: revoke ACE {path}: {exception.Message}");
            }
        }
    }

    private static string DecodeOutput(string? payloadBase64)
    {
        if (string.IsNullOrEmpty(payloadBase64))
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(payloadBase64));
    }
}
