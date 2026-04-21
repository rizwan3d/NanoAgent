using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.ConsoleHost.Prompts;
using NanoAgent.ConsoleHost.Rendering;
using NanoAgent.ConsoleHost.Terminal;
using NanoAgent.Tests.ConsoleHost.TestDoubles;
using FluentAssertions;
using Moq;
using System.Text;

namespace NanoAgent.Tests.ConsoleHost.Prompts;

public sealed class ConsoleSelectionPromptTests
{
    [Fact]
    public async Task PromptAsync_Should_ReturnSelectedValue_When_UserNavigatesWithArrowKeys()
    {
        FakeConsoleTerminal terminal = new();
        terminal.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        terminal.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        ConsolePromptRenderer renderer = new(terminal, SpectreConsoleFactory.Create(terminal));
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        ConsoleSelectionPrompt sut = new(new ConsoleInteractionGate(), terminal, renderer, statusMessageWriter.Object);

        SelectionPromptRequest<string> request = new(
            "Choose a provider",
            [
                new SelectionPromptOption<string>("OpenAI", "openai"),
                new SelectionPromptOption<string>("Compatible", "compatible")
            ],
            "Pick the provider to configure.",
            DefaultIndex: 0);

        string selectedValue = await sut.PromptAsync(request, CancellationToken.None);

        selectedValue.Should().Be("compatible");
    }

    [Fact]
    public async Task PromptAsync_Should_UseFallbackDefault_When_InteractiveControlsAreUnavailable_And_InputIsBlank()
    {
        FakeConsoleTerminal terminal = new()
        {
            IsInputRedirected = true,
            IsOutputRedirected = true
        };
        terminal.EnqueueLine(string.Empty);

        ConsolePromptRenderer renderer = new(terminal, SpectreConsoleFactory.Create(terminal));
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        ConsoleSelectionPrompt sut = new(new ConsoleInteractionGate(), terminal, renderer, statusMessageWriter.Object);

        SelectionPromptRequest<string> request = new(
            "Choose a provider",
            [
                new SelectionPromptOption<string>("OpenAI", "openai"),
                new SelectionPromptOption<string>("Compatible", "compatible")
            ],
            DefaultIndex: 1);

        string selectedValue = await sut.PromptAsync(request, CancellationToken.None);

        selectedValue.Should().Be("compatible");
    }

    [Fact]
    public async Task PromptAsync_Should_ThrowPromptCancelledException_When_EscapeIsPressed()
    {
        FakeConsoleTerminal terminal = new();
        terminal.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        ConsolePromptRenderer renderer = new(terminal, SpectreConsoleFactory.Create(terminal));
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        ConsoleSelectionPrompt sut = new(new ConsoleInteractionGate(), terminal, renderer, statusMessageWriter.Object);

        SelectionPromptRequest<string> request = new(
            "Choose a provider",
            [new SelectionPromptOption<string>("OpenAI", "openai")]);

        Func<Task> action = () => sut.PromptAsync(request, CancellationToken.None);

        await action.Should().ThrowAsync<PromptCancelledException>();
    }

    [Fact]
    public async Task PromptAsync_Should_NotThrow_When_RewriteEndsPastConsoleBuffer()
    {
        FakeConsoleTerminal innerTerminal = new();
        for (int index = 0; index < 33; index++)
        {
            innerTerminal.WriteLine();
        }

        innerTerminal.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        innerTerminal.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        ThrowingCursorConsoleTerminal terminal = new(innerTerminal, top => top >= 40);
        ConsolePromptRenderer renderer = new(terminal, SpectreConsoleFactory.Create(terminal));
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        ConsoleSelectionPrompt sut = new(new ConsoleInteractionGate(), terminal, renderer, statusMessageWriter.Object);

        SelectionPromptRequest<string> request = new(
            "Choose a provider",
            [
                new SelectionPromptOption<string>("OpenAI", "openai"),
                new SelectionPromptOption<string>("Compatible", "compatible"),
                new SelectionPromptOption<string>("Local", "local"),
                new SelectionPromptOption<string>("Custom", "custom")
            ],
            DefaultIndex: 0);

        string selectedValue = await sut.PromptAsync(request, CancellationToken.None);

        selectedValue.Should().Be("compatible");
    }

    [Fact]
    public async Task PromptAsync_Should_StartOnNewLine_When_CursorIsMidLine()
    {
        FakeConsoleTerminal terminal = new();
        terminal.Write("Working 14 tokens est.  Esc to interrupt");
        terminal.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        ConsolePromptRenderer renderer = new(terminal, SpectreConsoleFactory.Create(terminal));
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        ConsoleSelectionPrompt sut = new(new ConsoleInteractionGate(), terminal, renderer, statusMessageWriter.Object);

        SelectionPromptRequest<string> request = new(
            "Choose a provider",
            [new SelectionPromptOption<string>("OpenAI", "openai")]);

        string selectedValue = await sut.PromptAsync(request, CancellationToken.None);
        terminal.WriteLine("Edited 1 file");

        selectedValue.Should().Be("openai");
        terminal.Output.Should().Contain("Working 14 tokens est.  Esc to interrupt");
        terminal.Output.Should().Contain($"{Environment.NewLine}Edited 1 file");
        terminal.Output.Should().NotContain("interruptEdited 1 file");
    }

    [Fact]
    public async Task PromptAsync_Should_ClearInteractivePromptLines_BeforeLaterOutputReusesThem()
    {
        FakeConsoleTerminal terminal = new();
        terminal.Write("Working 55s - 229 tokens est.  Esc to interrupt");
        int statusTop = terminal.CursorTop;
        terminal.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        ConsolePromptRenderer renderer = new(terminal, SpectreConsoleFactory.Create(terminal));
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        ConsoleSelectionPrompt sut = new(new ConsoleInteractionGate(), terminal, renderer, statusMessageWriter.Object);

        SelectionPromptRequest<string> request = new(
            "Approve file write?",
            [
                new SelectionPromptOption<string>("Allow once", "allow-once", "Run this request now without saving an override."),
                new SelectionPromptOption<string>("Allow for NanoAgent", "allow-agent", "Remember an allow override for this exact pattern on the current agent."),
                new SelectionPromptOption<string>("Deny once", "deny-once", "Block this request now but keep prompting in the future."),
                new SelectionPromptOption<string>("Deny for NanoAgent", "deny-agent", "Remember a deny override for this exact pattern on the current agent.")
            ],
            "Permission requires approval for tool 'file_write' to write file 'index.html'.\n\nTool: file_write\nFile path: index.html",
            DefaultIndex: 0);

        string selectedValue = await sut.PromptAsync(request, CancellationToken.None);

        terminal.SetCursorPosition(0, statusTop);
        terminal.WriteLine("Edited 1 file (+23 -0)");
        terminal.WriteLine("  - index.html (+23 -0)");
        terminal.WriteLine("     1 +<!DOCTYPE html>");

        selectedValue.Should().Be("allow-once");
        terminal.Output.Should().Contain("Edited 1 file (+23 -0)");
        terminal.Output.Should().NotContain("Allow for NanoAgent");
        terminal.Output.Should().NotContain("Deny for NanoAgent");
        terminal.Output.Should().NotContain("l for tool 'file_write' to write file 'index.html'");
    }

    [Fact]
    public async Task PromptAsync_Should_DeletePromptRows_InsteadOfLeavingBlankBlock()
    {
        FakeConsoleTerminal terminal = new();
        terminal.WriteLine("Edited 1 file (+23 -0)");
        terminal.WriteLine("  - index.html (+23 -0)");
        terminal.Write("Working 55s - 229 tokens est.  Esc to interrupt");
        terminal.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        ConsolePromptRenderer renderer = new(terminal, SpectreConsoleFactory.Create(terminal));
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        ConsoleSelectionPrompt sut = new(new ConsoleInteractionGate(), terminal, renderer, statusMessageWriter.Object);

        SelectionPromptRequest<string> request = new(
            "Approve file write?",
            [
                new SelectionPromptOption<string>("Allow once", "allow-once", "Run this request now without saving an override."),
                new SelectionPromptOption<string>("Allow for NanoAgent", "allow-agent", "Remember an allow override for this exact pattern on the current agent."),
                new SelectionPromptOption<string>("Deny once", "deny-once", "Block this request now but keep prompting in the future."),
                new SelectionPromptOption<string>("Deny for NanoAgent", "deny-agent", "Remember a deny override for this exact pattern on the current agent.")
            ],
            "Permission requires approval for tool 'file_write' to write file 'style.css'.\n\nTool: file_write\nFile path: style.css",
            DefaultIndex: 0);

        string selectedValue = await sut.PromptAsync(request, CancellationToken.None);

        terminal.WriteLine("Edited 1 file (+110 -0)");
        terminal.WriteLine("  - style.css (+110 -0)");

        selectedValue.Should().Be("allow-once");
        terminal.Output.Should().Contain("Edited 1 file (+23 -0)");
        terminal.Output.Should().Contain("Edited 1 file (+110 -0)");
        terminal.Output.Should().NotContain($"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}");
        terminal.Output.Should().NotContain("Approve file write?");
    }

    [Fact]
    public void RewriteSelectionOptions_Should_NotDuplicate_When_PromptRenderScrolled()
    {
        ScrollingConsoleTerminal terminal = new(maxCursorTop: 6);
        ConsolePromptRenderer renderer = new(terminal, SpectreConsoleFactory.Create(terminal));

        SelectionPromptRequest<string> request = new(
            "Approve file write?",
            [
                new SelectionPromptOption<string>("Allow once", "allow-once", "Run this request now without saving an override."),
                new SelectionPromptOption<string>("Allow for NanoAgent", "allow-agent", "Remember an allow override for this exact pattern on the current agent."),
                new SelectionPromptOption<string>("Deny once", "deny-once", "Block this request now but keep prompting in the future."),
                new SelectionPromptOption<string>("Deny for NanoAgent", "deny-agent", "Remember a deny override for this exact pattern on the current agent.")
            ],
            "Permission requires approval for tool 'file_write' to write file 'style.css'.\n\nTool: file_write\nFile path: style.css",
            DefaultIndex: 2);

        InteractiveSelectionPromptLayout layout = renderer.WriteInteractiveSelectionPrompt(request, selectedIndex: 2);
        renderer.RewriteSelectionOptions(request, selectedIndex: 1, layout);
        renderer.RewriteSelectionOptions(request, selectedIndex: 0, layout);

        string output = terminal.Output;
        CountOccurrences(output, "Allow once").Should().Be(1);
        CountOccurrences(output, "Allow for NanoAgent").Should().Be(1);
        CountOccurrences(output, "Deny once").Should().Be(1);
        CountOccurrences(output, "Deny for NanoAgent").Should().Be(1);
        CountOccurrences(output, "> Allow once").Should().Be(1);
    }

    private static int CountOccurrences(string value, string pattern)
    {
        int count = 0;
        int index = 0;

        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private sealed class ThrowingCursorConsoleTerminal : IConsoleTerminal
    {
        private readonly FakeConsoleTerminal _inner;
        private readonly Func<int, bool> _shouldThrowForTop;

        public ThrowingCursorConsoleTerminal(
            FakeConsoleTerminal inner,
            Func<int, bool> shouldThrowForTop)
        {
            _inner = inner;
            _shouldThrowForTop = shouldThrowForTop;
        }

        public ConsoleColor BackgroundColor
        {
            get => _inner.BackgroundColor;
            set => _inner.BackgroundColor = value;
        }

        public int CursorLeft => _inner.CursorLeft;

        public int CursorTop => _inner.CursorTop;

        public ConsoleColor ForegroundColor
        {
            get => _inner.ForegroundColor;
            set => _inner.ForegroundColor = value;
        }

        public bool IsInputRedirected => _inner.IsInputRedirected;

        public bool IsOutputRedirected => _inner.IsOutputRedirected;

        public bool KeyAvailable => _inner.KeyAvailable;

        public int WindowHeight => _inner.WindowHeight;

        public int WindowWidth => _inner.WindowWidth;

        public ConsoleKeyInfo ReadKey(bool intercept)
        {
            return _inner.ReadKey(intercept);
        }

        public string? ReadLine()
        {
            return _inner.ReadLine();
        }

        public void ResetColor()
        {
            _inner.ResetColor();
        }

        public void SetCursorPosition(int left, int top)
        {
            if (_shouldThrowForTop(top))
            {
                throw new ArgumentOutOfRangeException(nameof(top));
            }

            _inner.SetCursorPosition(left, top);
        }

        public void Write(string value)
        {
            _inner.Write(value);
        }

        public void WriteLine()
        {
            _inner.WriteLine();
        }

        public void WriteLine(string value)
        {
            _inner.WriteLine(value);
        }
    }

    private sealed class ScrollingConsoleTerminal : IConsoleTerminal
    {
        private readonly List<ConsoleLine> _lines;
        private readonly int _maxCursorTop;
        private int _cursorLeft;

        public ScrollingConsoleTerminal(int maxCursorTop)
        {
            _maxCursorTop = maxCursorTop;
            _lines = Enumerable.Range(0, maxCursorTop + 1)
                .Select(static _ => new ConsoleLine())
                .ToList();
        }

        public ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;

        public int CursorLeft => _cursorLeft;

        public int CursorTop { get; private set; }

        public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;

        public bool IsInputRedirected => false;

        public bool IsOutputRedirected => false;

        public bool KeyAvailable => false;

        public string Output => BuildOutput();

        public int WindowHeight => _maxCursorTop + 1;

        public int WindowWidth => 120;

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
            _cursorLeft = Math.Max(0, left);
        }

        public void Write(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            string normalized = value;
            StringBuilder segmentBuilder = new();

            for (int index = 0; index < normalized.Length; index++)
            {
                switch (normalized[index])
                {
                    case '\r':
                        FlushSegment(segmentBuilder);
                        _cursorLeft = 0;
                        break;

                    case '\n':
                        FlushSegment(segmentBuilder);
                        WriteLine();
                        break;

                    default:
                        segmentBuilder.Append(normalized[index]);
                        break;
                }
            }

            FlushSegment(segmentBuilder);
        }

        public void WriteLine()
        {
            _lines[CursorTop].HasTrailingNewLine = true;
            if (CursorTop == _maxCursorTop)
            {
                Scroll();
            }
            else
            {
                CursorTop++;
            }

            _cursorLeft = 0;
        }

        public void WriteLine(string value)
        {
            Write(value);
            WriteLine();
        }

        private string BuildOutput()
        {
            StringBuilder builder = new();
            for (int index = 0; index < _lines.Count; index++)
            {
                builder.Append(_lines[index].Text);
                if (_lines[index].HasTrailingNewLine)
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private void Scroll()
        {
            _lines.RemoveAt(0);
            _lines.Add(new ConsoleLine());
        }

        private void WriteSegment(string value)
        {
            if (value.Length == 0)
            {
                return;
            }

            StringBuilder line = _lines[CursorTop].Text;
            while (line.Length < _cursorLeft)
            {
                line.Append(' ');
            }

            foreach (char character in value)
            {
                if (_cursorLeft < line.Length)
                {
                    line[_cursorLeft] = character;
                }
                else
                {
                    line.Append(character);
                }

                _cursorLeft++;
            }
        }

        private void FlushSegment(StringBuilder segmentBuilder)
        {
            if (segmentBuilder.Length == 0)
            {
                return;
            }

            WriteSegment(segmentBuilder.ToString());
            segmentBuilder.Clear();
        }

        private sealed class ConsoleLine
        {
            public bool HasTrailingNewLine { get; set; }

            public StringBuilder Text { get; } = new();
        }
    }
}
