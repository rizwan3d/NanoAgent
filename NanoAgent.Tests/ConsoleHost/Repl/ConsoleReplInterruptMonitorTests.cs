using NanoAgent.Presentation.Cli.Repl;
using NanoAgent.Tests.ConsoleHost.TestDoubles;
using FluentAssertions;

namespace NanoAgent.Tests.ConsoleHost.Repl;

public sealed class ConsoleReplInterruptMonitorTests
{
    [Fact]
    public async Task StartMonitoringAsync_Should_CancelRequest_When_EscapeIsPressed()
    {
        FakeConsoleTerminal terminal = new();
        terminal.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        ConsoleReplInterruptMonitor sut = new(terminal);
        using CancellationTokenSource requestCancellationSource = new();

        await using IAsyncDisposable monitor = await sut.StartMonitoringAsync(
            requestCancellationSource,
            CancellationToken.None);

        bool interrupted = await WaitUntilAsync(
            () => requestCancellationSource.IsCancellationRequested,
            TimeSpan.FromSeconds(1));

        interrupted.Should().BeTrue();
    }

    private static async Task<bool> WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return predicate();
    }
}
