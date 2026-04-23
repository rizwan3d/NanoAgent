using NanoAgent.Application.Abstractions;
using NanoAgent.Presentation.Abstractions;

namespace NanoAgent.Presentation.Cli.Repl;

internal sealed class ConsoleReplInterruptMonitor : IReplInterruptMonitor
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly Terminal.IConsoleTerminal _terminal;

    public ConsoleReplInterruptMonitor(Terminal.IConsoleTerminal terminal)
    {
        _terminal = terminal;
    }

    public ValueTask<IAsyncDisposable> StartMonitoringAsync(
        CancellationTokenSource requestCancellationSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestCancellationSource);
        cancellationToken.ThrowIfCancellationRequested();

        if (_terminal.IsInputRedirected || _terminal.IsOutputRedirected)
        {
            return ValueTask.FromResult<IAsyncDisposable>(NoOpAsyncDisposable.Instance);
        }

        CancellationTokenSource monitorCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            requestCancellationSource.Token);

        Task monitorTask = MonitorAsync(
            requestCancellationSource,
            monitorCancellationSource.Token);

        return ValueTask.FromResult<IAsyncDisposable>(
            new MonitorScope(
                monitorCancellationSource,
                monitorTask));
    }

    private async Task MonitorAsync(
        CancellationTokenSource requestCancellationSource,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_terminal.KeyAvailable)
                {
                    ConsoleKeyInfo keyInfo = _terminal.ReadKey(intercept: true);
                    if (keyInfo.Key == ConsoleKey.Escape)
                    {
                        requestCancellationSource.Cancel();
                        return;
                    }
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed class MonitorScope : IAsyncDisposable
    {
        private readonly CancellationTokenSource _monitorCancellationSource;
        private readonly Task _monitorTask;

        public MonitorScope(
            CancellationTokenSource monitorCancellationSource,
            Task monitorTask)
        {
            _monitorCancellationSource = monitorCancellationSource;
            _monitorTask = monitorTask;
        }

        public async ValueTask DisposeAsync()
        {
            _monitorCancellationSource.Cancel();

            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _monitorCancellationSource.Dispose();
            }
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static NoOpAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
