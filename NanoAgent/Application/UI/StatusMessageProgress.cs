using NanoAgent.Application.Abstractions;

namespace NanoAgent.Application.UI;

/// <summary>
/// Adapts <see cref="IStatusMessageWriter"/> to <see cref="IProgress{T}"/> so long-running
/// operations (such as installing an update) can stream their output to the UI as info
/// messages line by line.
/// </summary>
/// <remarks>
/// Reports synchronously on the calling thread, unlike <see cref="Progress{T}"/> which posts
/// to a captured synchronization context. In a console host there is no such context, so
/// <see cref="Progress{T}"/> would defer callbacks to the thread pool and the lines could
/// render out of order or after the final result. Reporting inline keeps them ordered.
/// </remarks>
public sealed class StatusMessageProgress : IProgress<string>
{
    private readonly IStatusMessageWriter _statusMessageWriter;

    public StatusMessageProgress(IStatusMessageWriter statusMessageWriter)
    {
        ArgumentNullException.ThrowIfNull(statusMessageWriter);
        _statusMessageWriter = statusMessageWriter;
    }

    public void Report(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // Progress is best-effort and must never disrupt the underlying operation, which
        // may be reporting from a background reader thread. CancellationToken.None ensures a
        // cancelled operation can still flush its trailing lines; the writer completes
        // synchronously, so discarding the task does not reorder output.
        try
        {
            _ = _statusMessageWriter.ShowInfoAsync(value, CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }
}
