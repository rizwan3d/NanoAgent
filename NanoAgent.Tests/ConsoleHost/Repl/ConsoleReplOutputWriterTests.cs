using NanoAgent.Application.Models;
using NanoAgent.ConsoleHost.Rendering;
using NanoAgent.ConsoleHost.Repl;
using NanoAgent.ConsoleHost.Terminal;
using NanoAgent.Tests.ConsoleHost.TestDoubles;
using FluentAssertions;
using System.Text;

namespace NanoAgent.Tests.ConsoleHost.Repl;

public sealed class ConsoleReplOutputWriterTests
{
    [Fact]
    public async Task WriteShellHeaderAsync_Should_RenderBanner_When_ShellStarts()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleCliOutputTarget outputTarget = new(terminal);
        ConsoleReplOutputWriter sut = new(
            new MarkdownLikeCliMessageFormatter(),
            new CliTextRenderer(outputTarget),
            outputTarget,
            terminal);

        await sut.WriteShellHeaderAsync("NanoAgent", "gpt-oss-20b", CancellationToken.None);

        terminal.Output.Should().Contain("NanoAgent");
        terminal.Output.Should().Contain("Model: gpt-oss-20b");
        terminal.Output.Should().Contain("GitHub: github.com/rizwan3d/NanoAgent");
        terminal.Output.Should().Contain("Sponsor: ALFAIN Technologies (PVT) Limited (https://alfain.co/)");
        terminal.Output.Should().Contain("Press Ctrl+C or use /exit to quit.");
        terminal.Output.Should().Contain(new string('\u2500', 53));
    }

    [Fact]
    public async Task WriteResponseAsync_Should_RenderMetricsFooter_When_MetricsAreProvided()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleCliOutputTarget outputTarget = new(terminal);
        ConsoleReplOutputWriter sut = new(
            new MarkdownLikeCliMessageFormatter(),
            new CliTextRenderer(outputTarget),
            outputTarget,
            terminal);

        await sut.WriteResponseAsync(
            "Done.",
            new ConversationTurnMetrics(TimeSpan.FromSeconds(4), 14, 26),
            CancellationToken.None);

        terminal.Output.Should().Contain("assistant");
        terminal.Output.Should().Contain("Done.");
        terminal.Output.Should().Contain("(4s \u00B7 \u2193 26 tokens est.)");
    }

    [Fact]
    public async Task BeginResponseProgressAsync_Should_RenderProgressLine_When_OutputIsInteractive()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleCliOutputTarget outputTarget = new(terminal);
        ConsoleReplOutputWriter sut = new(
            new MarkdownLikeCliMessageFormatter(),
            new CliTextRenderer(outputTarget),
            outputTarget,
            terminal);

        await using IAsyncDisposable progress = await sut.BeginResponseProgressAsync(14, 0, CancellationToken.None);

        terminal.Output.Should().Contain("\u2193 14 tokens est.");
    }

    [Fact]
    public async Task BeginResponseProgressAsync_Should_UpdateEstimatedTokenCount_When_RequestIsStillRunning()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleCliOutputTarget outputTarget = new(terminal);
        ConsoleReplOutputWriter sut = new(
            new MarkdownLikeCliMessageFormatter(),
            new CliTextRenderer(outputTarget),
            outputTarget,
            terminal);

        await using IAsyncDisposable progress = await sut.BeginResponseProgressAsync(14, 10, CancellationToken.None);
        await Task.Delay(350);

        terminal.Output.Should().Contain("\u2193 24 tokens est.");
        terminal.Output.Should().MatchRegex(@".*\u2193 2[5-9] tokens est\..*");
    }

    [Fact]
    public async Task BeginResponseProgressAsync_Should_NotThrow_When_ProgressLineStartsOnLastConsoleRow()
    {
        BoundedConsoleTerminal terminal = new(initialCursorTop: 36, maxCursorTop: 36);
        ConsoleCliOutputTarget outputTarget = new(terminal);
        ConsoleReplOutputWriter sut = new(
            new MarkdownLikeCliMessageFormatter(),
            new CliTextRenderer(outputTarget),
            outputTarget,
            terminal);

        Func<Task> action = async () =>
        {
            await using IAsyncDisposable progress = await sut.BeginResponseProgressAsync(14, 0, CancellationToken.None);
            await Task.Delay(350);
        };

        await action.Should().NotThrowAsync();
        terminal.Output.Should().Contain("\u2193 14 tokens est.");
    }

    private sealed class BoundedConsoleTerminal : IConsoleTerminal
    {
        private readonly StringBuilder _outputBuilder = new();
        private readonly int _maxCursorTop;

        public BoundedConsoleTerminal(int initialCursorTop, int maxCursorTop)
        {
            CursorTop = initialCursorTop;
            _maxCursorTop = maxCursorTop;
        }

        public ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;

        public int CursorTop { get; private set; }

        public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;

        public bool IsInputRedirected => false;

        public bool IsOutputRedirected => false;

        public int WindowWidth => 120;

        public string Output => _outputBuilder.ToString();

        public ConsoleKeyInfo ReadKey(bool intercept)
        {
            throw new NotSupportedException();
        }

        public string? ReadLine()
        {
            throw new NotSupportedException();
        }

        public void ResetColor()
        {
        }

        public void SetCursorPosition(int left, int top)
        {
            if (top < 0 || top > _maxCursorTop)
            {
                throw new ArgumentOutOfRangeException(nameof(top));
            }

            CursorTop = top;
        }

        public void Write(string value)
        {
            _outputBuilder.Append(value);
        }

        public void WriteLine()
        {
            _outputBuilder.AppendLine();
            if (CursorTop < _maxCursorTop)
            {
                CursorTop++;
            }
        }

        public void WriteLine(string value)
        {
            _outputBuilder.AppendLine(value);
            if (CursorTop < _maxCursorTop)
            {
                CursorTop++;
            }
        }
    }
}
