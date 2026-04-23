using NanoAgent.Presentation.Cli.Rendering;
using NanoAgent.Presentation.Cli.Terminal;
using NanoAgent.Tests.ConsoleHost.TestDoubles;
using FluentAssertions;

namespace NanoAgent.Tests.ConsoleHost.Terminal;

public sealed class ConsolePromptInputReaderTests
{
    [Fact]
    public async Task ReadLineAsync_Should_ReturnMaskedSecretWithoutEchoingPlainText()
    {
        FakeConsoleTerminal terminal = new();
        terminal.EnqueueKey(new ConsoleKeyInfo('s', ConsoleKey.S, false, false, false));
        terminal.EnqueueKey(new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false));
        terminal.EnqueueKey(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false));
        terminal.EnqueueKey(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));
        terminal.EnqueueKey(new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false));
        terminal.EnqueueKey(new ConsoleKeyInfo('t', ConsoleKey.T, false, false, false));
        terminal.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        ConsolePromptInputReader sut = new(terminal, SpectreConsoleFactory.Create(terminal));

        string result = await sut.ReadLineAsync(
            defaultValue: null,
            ConsoleInputEchoMode.SecretMask,
            allowCancellation: true,
            CancellationToken.None);

        result.Should().Be("s3cr3t");
        terminal.Output.Should().Contain("******");
        terminal.Output.Should().NotContain("s3cr3t");
    }
}
