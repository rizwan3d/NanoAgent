namespace NanoAgent.Infrastructure.Secrets;

/// <summary>
/// Abstracts a long-running ("background") process whose standard output and error are
/// streamed incrementally. Implementations cover a plain local <see cref="System.Diagnostics.Process"/>
/// and a Windows OS-sandboxed child launched over the sandbox runner IPC channel.
/// The owning terminal buffers the streamed text; the handle only produces chunks,
/// reports exit, and supports termination.
/// </summary>
internal interface IBackgroundProcessHandle : IDisposable
{
    /// <summary>Whether the underlying process has exited.</summary>
    bool HasExited { get; }

    /// <summary>The exit code. Only meaningful once <see cref="HasExited"/> is <see langword="true"/>.</summary>
    int ExitCode { get; }

    /// <summary>Raised once when the underlying process exits.</summary>
    event EventHandler Exited;

    /// <summary>
    /// Begins streaming standard output and error. Each chunk is delivered to the matching
    /// callback as it arrives. Must be called at most once.
    /// </summary>
    void StartStreaming(Action<string> onStandardOutput, Action<string> onStandardError);

    /// <summary>Waits asynchronously for the process to exit.</summary>
    Task WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>Waits up to <paramref name="milliseconds"/> for the process to exit; returns whether it exited.</summary>
    bool WaitForExit(int milliseconds);

    /// <summary>Waits for the output/error stream pumps to drain after the process has exited.</summary>
    Task CompleteStreamingAsync(CancellationToken cancellationToken);

    /// <summary>Terminates the process (and its descendants where supported).</summary>
    void Kill();
}
