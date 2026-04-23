namespace NanoAgent.Presentation.Abstractions;

public interface IReplInterruptMonitor
{
    ValueTask<IAsyncDisposable> StartMonitoringAsync(
        CancellationTokenSource requestCancellationSource,
        CancellationToken cancellationToken);
}
