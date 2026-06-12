using System.ComponentModel;
using System.Diagnostics;

namespace NanoAgent.Infrastructure.Secrets;

/// <summary>
/// <see cref="IBackgroundProcessHandle"/> backed by a local <see cref="Process"/> with redirected
/// standard output and error. This preserves the original (non-sandboxed) background terminal behavior.
/// </summary>
internal sealed class LocalProcessBackgroundHandle : IBackgroundProcessHandle
{
    private readonly Process _process;
    private Task _standardOutputTask = Task.CompletedTask;
    private Task _standardErrorTask = Task.CompletedTask;

    public LocalProcessBackgroundHandle(Process process)
    {
        _process = process;
        _process.Exited += OnProcessExited;
    }

    public event EventHandler? Exited;

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int ExitCode
    {
        get
        {
            try
            {
                return _process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }
    }

    public void StartStreaming(
        Action<string> onStandardOutput,
        Action<string> onStandardError)
    {
        _standardOutputTask = ReadStreamAsync(_process.StandardOutput, onStandardOutput);
        _standardErrorTask = ReadStreamAsync(_process.StandardError, onStandardError);
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

    public bool WaitForExit(int milliseconds)
    {
        try
        {
            return _process.WaitForExit(milliseconds);
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    public Task CompleteStreamingAsync(CancellationToken cancellationToken)
    {
        return Task.WhenAll(_standardOutputTask, _standardErrorTask).WaitAsync(cancellationToken);
    }

    public void Kill()
    {
        try
        {
            if (!HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    public void Dispose()
    {
        _process.Exited -= OnProcessExited;
        _process.Dispose();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        Exited?.Invoke(this, EventArgs.Empty);
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
