using NanoAgent.ConsoleHost.Repl;
using NanoAgent.Tests.ConsoleHost.TestDoubles;
using FluentAssertions;

namespace NanoAgent.Tests.ConsoleHost.Repl;

public sealed class ConsoleReplInputReaderTests
{
    [Fact]
    public async Task ReadLineAsync_Should_ReturnSingleLineInput()
    {
        FakeConsoleTerminal terminal = new();
        terminal.EnqueueLine("build the feature");

        ConsoleReplInputReader sut = new(terminal);

        string? result = await sut.ReadLineAsync(CancellationToken.None);

        result.Should().Be("build the feature");
        terminal.Output.Should().Contain("nanoagent> ");
    }

    [Fact]
    public async Task ReadLineAsync_Should_ReturnMultilineInput_When_TripleQuoteBlockIsUsed()
    {
        FakeConsoleTerminal terminal = new();
        terminal.EnqueueLine("\"\"\"");
        terminal.EnqueueLine("Line one");
        terminal.EnqueueLine("Line two");
        terminal.EnqueueLine("\"\"\"");

        ConsoleReplInputReader sut = new(terminal);

        string? result = await sut.ReadLineAsync(CancellationToken.None);

        result.Should().Be($"Line one{Environment.NewLine}Line two");
        terminal.Output.Should().Contain("nanoagent> ");
        terminal.Output.Should().Contain("...> ");
    }

    [Fact]
    public async Task ReadLineAsync_Should_ReturnCollectedLines_When_StreamClosesInsideMultilineInput()
    {
        FakeConsoleTerminal terminal = new();
        terminal.EnqueueLine("\"\"\"");
        terminal.EnqueueLine("Partial line");
        terminal.EnqueueLine(null);

        ConsoleReplInputReader sut = new(terminal);

        string? result = await sut.ReadLineAsync(CancellationToken.None);

        result.Should().Be("Partial line");
    }
}
