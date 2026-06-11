using System.Diagnostics;

namespace NanoAgent.Desktop.Services;

public class GitService
{
    /// <summary>
    /// Default time a single git invocation is allowed to run before it is
    /// considered hung and forcibly terminated.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (!Directory.Exists(workingDirectory))
        {
            return [];
        }

        GitResult result;
        try
        {
            result = await ExecuteAsync(
                workingDirectory,
                ["status", "--short"],
                cancellationToken,
                timeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A hung "git status" should not surface changed files; the caller
            // is expected to continue without crashing the UI.
            return [];
        }

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return [];
        }

        var files = new List<string>();
        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length <= 3)
            {
                continue;
            }

            files.Add(trimmed[3..]);
        }

        return files;
    }

    /// <summary>
    /// Runs git with the provided arguments and returns the captured result.
    /// The operation is bounded by <paramref name="timeout"/> (default 30s) and
    /// honors <paramref name="cancellationToken"/>. If either fires the git
    /// process (and its child processes) are killed and a
    /// <see cref="TimeoutException"/> or <see cref="OperationCanceledException"/>
    /// is thrown.
    /// </summary>
    public async Task<GitResult> ExecuteAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var effectiveTimeout = timeout ?? DefaultTimeout;

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new GitResult(-1, string.Empty, string.Empty);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (effectiveTimeout != Timeout.InfiniteTimeSpan)
        {
            timeoutCts.CancelAfter(effectiveTimeout);
        }

        // Read both streams concurrently to avoid pipe-buffer deadlocks on
        // large output, and await exit so the process is fully drained.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return new GitResult(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            // Distinguish a caller-requested cancellation from a timeout.
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new TimeoutException(
                $"Git operation timed out after {effectiveTimeout.TotalSeconds:0.###}s: git {string.Join(' ', arguments)}");
        }
    }

    private static void TryKill(Process process)
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
            // Process already exited between the check and the kill.
        }
        catch (NotSupportedException)
        {
            // Killing the process tree is unsupported on this platform; ignore.
        }
    }
}

/// <summary>
/// Result of a git invocation: the process exit code and captured streams.
/// </summary>
public readonly record struct GitResult(int ExitCode, string StandardOutput, string StandardError);
