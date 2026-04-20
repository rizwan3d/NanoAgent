using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.ConsoleHost.Rendering;
using NanoAgent.ConsoleHost.Repl;
using NanoAgent.ConsoleHost.Terminal;
using NanoAgent.Tests.ConsoleHost.TestDoubles;
using FluentAssertions;
using System.Text;
using System.Text.RegularExpressions;

namespace NanoAgent.Tests.ConsoleHost.Repl;

public sealed class ConsoleReplOutputWriterTests
{
    [Fact]
    public async Task WriteShellHeaderAsync_Should_RenderBanner_When_ShellStarts()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        await sut.WriteShellHeaderAsync("NanoAgent", "gpt-oss-20b", CancellationToken.None);

        string output = GetPlainOutput(terminal.Output);
        output.Should().Contain("NanoAgent");
        output.Should().Contain("Model: gpt-oss-20b");
        output.Should().Contain("GitHub: github.com/rizwan3d/NanoAgent");
        output.Should().Contain("Sponsor: ALFAIN Technologies (PVT) Limited (https://alfain.co/)");
        output.Should().Contain("Press Ctrl+C or use /exit to quit.");
        output.Should().Contain("Press Esc while a response is running to interrupt the current request.");
        output.Should().Contain(new string('\u2500', 53));
    }

    [Fact]
    public async Task WriteResponseAsync_Should_RenderMetricsFooter_When_MetricsAreProvided()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        await sut.WriteResponseAsync(
            "Done.",
            new ConversationTurnMetrics(TimeSpan.FromSeconds(4), 14, 26),
            CancellationToken.None);

        string output = GetPlainOutput(terminal.Output);
        output.Should().Contain("assistant");
        output.Should().Contain("Done.");
        output.Should().Contain("(4s \u00B7 26 tokens est.)");
    }

    [Fact]
    public async Task WriteResponseAsync_Should_CompactLargeMetricValues_When_MetricsAreProvided()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        await sut.WriteResponseAsync(
            "Done.",
            new ConversationTurnMetrics(TimeSpan.FromSeconds(3665), 14, 1234),
            CancellationToken.None);

        GetPlainOutput(terminal.Output).Should().Contain("(1h 1m 5s \u00B7 1.2k tokens est.)");
    }

    [Fact]
    public async Task BeginResponseProgressAsync_Should_KeepToolOutputVisible_When_ToolExecutionCompletes()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        ToolExecutionBatchResult toolExecutionResult = new([
            new ToolInvocationResult(
                "call_1",
                "shell_command",
                ToolResultFactory.Success(
                    "Ran Get-ChildItem -Force.",
                    new ShellCommandExecutionResult(
                        "Get-ChildItem -Force",
                        ".",
                        0,
                        string.Empty,
                        ". : File C:\\Users\\allga\\Documents\\WindowsPowerShell\\Microsoft.PowerShell_profile.ps1 cannot be loaded because\r\nrunning"),
                    ToolJsonContext.Default.ShellCommandExecutionResult)),
            new ToolInvocationResult(
                "call_2",
                "file_write",
                ToolResultFactory.Success(
                    "Created index.html.",
                    new WorkspaceFileWriteResult(
                        "index.html",
                        false,
                        42,
                        4,
                        0,
                        [
                            new WorkspaceFileWritePreviewLine(1, "add", "<!DOCTYPE html>"),
                            new WorkspaceFileWritePreviewLine(2, "add", "<html lang=\"en\">"),
                            new WorkspaceFileWritePreviewLine(3, "add", "<body>"),
                            new WorkspaceFileWritePreviewLine(4, "add", "</body>")
                        ],
                        0),
                    ToolJsonContext.Default.WorkspaceFileWriteResult)),
            new ToolInvocationResult(
                "call_3",
                "file_write",
                ToolResultFactory.Success(
                    "Updated styles.css.",
                    new WorkspaceFileWriteResult(
                        "styles.css",
                        true,
                        21,
                        1,
                        1,
                        [
                            new WorkspaceFileWritePreviewLine(7, "context", ".card {"),
                            new WorkspaceFileWritePreviewLine(8, "remove", "  color: red;"),
                            new WorkspaceFileWritePreviewLine(8, "add", "  color: blue;")
                        ],
                        0),
                    ToolJsonContext.Default.WorkspaceFileWriteResult))
        ]);

        await using IResponseProgress progress = await sut.BeginResponseProgressAsync(14, 0, CancellationToken.None);
        await progress.ReportToolCallsStartedAsync([
            new ConversationToolCall("call_1", "shell_command", """{"command":"Get-ChildItem -Force"}"""),
            new ConversationToolCall("call_2", "file_write", """{"path":"index.html"}""")
        ], CancellationToken.None);
        await progress.ReportToolResultsAsync(toolExecutionResult, CancellationToken.None);

        string output = GetPlainOutput(terminal.Output);
        output.Should().Contain("\u2022 Ran Get-ChildItem -Force");
        output.Should().Contain("\u2514 . : File C:\\Users\\allga\\Documents\\WindowsPowerShell\\Microsoft.PowerShell_profile.ps1 cannot be loaded because");
        output.Should().Contain("\u2022 Edited 2 files (+5 -1)");
        output.Should().Contain("\u2514 index.html (+4 -0)");
        output.Should().Contain("1 +<!DOCTYPE html>");
        output.Should().Contain("\u2026 +1 more file");
    }

    [Fact]
    public async Task WriteResponseAsync_Should_NotLeaveWorkingLineInTranscript_When_ProgressCompletes()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        await using (IResponseProgress progress = await sut.BeginResponseProgressAsync(14, 0, CancellationToken.None))
        {
            await progress.ReportToolResultsAsync(
                new ToolExecutionBatchResult([
                    new ToolInvocationResult(
                        "call_1",
                        "file_write",
                        ToolResultFactory.Success(
                            "Created index.html.",
                            new WorkspaceFileWriteResult(
                                "index.html",
                                false,
                                42,
                                1,
                                0,
                                [new WorkspaceFileWritePreviewLine(1, "add", "<!DOCTYPE html>")],
                                0),
                            ToolJsonContext.Default.WorkspaceFileWriteResult))
                ]),
                CancellationToken.None);
        }

        await sut.WriteResponseAsync(
            "Done.",
            new ConversationTurnMetrics(TimeSpan.FromSeconds(2), 14, 14),
            CancellationToken.None);

        string output = terminal.Output;
        output.Should().NotContain("Working", "the live status line should not remain in the transcript after completion");
        output.Should().Contain("Done.");
    }

    [Fact]
    public async Task BeginResponseProgressAsync_Should_RenderProgressLine_When_OutputIsInteractive()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        await using IResponseProgress progress = await sut.BeginResponseProgressAsync(14, 0, CancellationToken.None);

        string output = GetPlainOutput(terminal.Output);
        output.Should().Contain("Working");
        output.Should().Contain("14 tokens est.");
        output.Should().Contain("Esc to interrupt");
    }

    [Fact]
    public async Task BeginResponseProgressAsync_Should_NotThrow_When_DirectoryListPayloadOmitsEntries()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        await using IResponseProgress progress = await sut.BeginResponseProgressAsync(14, 0, CancellationToken.None);

        Func<Task> action = async () =>
        {
            await progress.ReportToolResultsAsync(
                new ToolExecutionBatchResult([
                    new ToolInvocationResult(
                        "call_1",
                        "directory_list",
                        ToolResult.Success(
                            "Listed directory 'src'.",
                            """{"Path":"src"}"""))
                ]),
                CancellationToken.None);
        };

        await action.Should().NotThrowAsync();
        string output = GetPlainOutput(terminal.Output);
        output.Should().Contain("Listed src");
        output.Should().Contain("(empty)");
    }

    [Fact]
    public async Task BeginResponseProgressAsync_Should_KeepInitialEstimatedTokenCount_When_RequestIsStillRunning()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        await using IResponseProgress progress = await sut.BeginResponseProgressAsync(14, 10, CancellationToken.None);
        await Task.Delay(350);

        string output = GetPlainOutput(terminal.Output);
        output.Should().Contain("24 tokens est.");
    }

    [Fact]
    public async Task BeginResponseProgressAsync_Should_NotThrow_When_ProgressLineStartsOnLastConsoleRow()
    {
        BoundedConsoleTerminal terminal = new(initialCursorTop: 36, maxCursorTop: 36);
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        Func<Task> action = async () =>
        {
            await using IResponseProgress progress = await sut.BeginResponseProgressAsync(14, 0, CancellationToken.None);
            await Task.Delay(350);
            GetPlainOutput(terminal.Output).Should().Contain("14 tokens est.");
        };

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BeginResponseProgressAsync_Should_RenderStatusLine_WithoutAnsiControlSequences()
    {
        RawCaptureConsoleTerminal terminal = new();
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        await using IResponseProgress progress = await sut.BeginResponseProgressAsync(14, 0, CancellationToken.None);
        await Task.Delay(350);

        terminal.RawOutput.Should().Contain("Working");
        terminal.RawOutput.Should().NotContain("\u001B[");
    }

    [Fact]
    public async Task BeginResponseProgressAsync_Should_NotLeaveSpacerRows_BetweenSequentialToolRounds()
    {
        FakeConsoleTerminal terminal = new();
        ConsoleReplOutputWriter sut = CreateSut(terminal);

        await using IResponseProgress progress = await sut.BeginResponseProgressAsync(14, 0, CancellationToken.None);

        await progress.ReportToolResultsAsync(
            CreateFileWriteBatchResult("call_1", "index.html", "<!DOCTYPE html>"),
            CancellationToken.None);
        await progress.ReportToolResultsAsync(
            CreateFileWriteBatchResult("call_2", "style.css", "body {"),
            CancellationToken.None);
        await progress.ReportToolResultsAsync(
            CreateFileWriteBatchResult("call_3", "script.js", "document.addEventListener('DOMContentLoaded', () => {"),
            CancellationToken.None);

        string output = GetPlainOutput(terminal.Output);

        output.Should().Contain("\u2514 index.html (+1 -0)");
        output.Should().Contain("\u2514 style.css (+1 -0)");
        output.Should().Contain("\u2514 script.js (+1 -0)");
        output.Should().NotContain(
            $"{Environment.NewLine}{Environment.NewLine}\u2022 Edited 1 file (+1 -0){Environment.NewLine}  \u2514 style.css (+1 -0)",
            "each new tool round should reuse the live status line row instead of leaving a blank spacer behind");
        output.Should().NotContain(
            $"{Environment.NewLine}{Environment.NewLine}\u2022 Edited 1 file (+1 -0){Environment.NewLine}  \u2514 script.js (+1 -0)",
            "repeated tool rounds should stay compact");
    }

    private static string GetPlainOutput(string value)
    {
        return Regex.Replace(
            value,
            @"\x1B\[[0-9;?]*[ -/]*[@-~]",
            string.Empty);
    }

    private static ToolExecutionBatchResult CreateFileWriteBatchResult(
        string toolCallId,
        string path,
        string previewText)
    {
        return new ToolExecutionBatchResult([
            new ToolInvocationResult(
                toolCallId,
                "file_write",
                ToolResultFactory.Success(
                    $"Created {path}.",
                    new WorkspaceFileWriteResult(
                        path,
                        false,
                        previewText.Length,
                        1,
                        0,
                        [new WorkspaceFileWritePreviewLine(1, "add", previewText)],
                        0),
                    ToolJsonContext.Default.WorkspaceFileWriteResult))
        ]);
    }

    private static ConsoleReplOutputWriter CreateSut(IConsoleTerminal terminal)
    {
        ConsoleRenderSettings settings = new()
        {
            EnableAnimations = false
        };

        var console = SpectreConsoleFactory.Create(terminal);
        ConsoleCliOutputTarget outputTarget = new(terminal, console);

        return new ConsoleReplOutputWriter(
            new MarkdownLikeCliMessageFormatter(),
            new CliTextRenderer(outputTarget, console, settings),
            outputTarget,
            console,
            new ConsoleInteractionGate(),
            terminal,
            settings);
    }

    private sealed class BoundedConsoleTerminal : IConsoleTerminal
    {
        private readonly List<ConsoleLine> _lines = [new ConsoleLine()];
        private readonly int _maxCursorTop;
        private int _cursorLeft;

        public BoundedConsoleTerminal(int initialCursorTop, int maxCursorTop)
        {
            CursorTop = initialCursorTop;
            _maxCursorTop = maxCursorTop;
        }

        public ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;

        public int CursorLeft => _cursorLeft;

        public int CursorTop { get; private set; }

        public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;

        public bool IsInputRedirected => false;

        public bool IsOutputRedirected => false;

        public bool KeyAvailable => false;

        public int WindowHeight => 37;

        public int WindowWidth => 120;

        public string Output => BuildOutput();

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

            EnsureLine(top);
            CursorTop = top;
            _cursorLeft = Math.Max(0, left);
        }

        public void Write(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            StringBuilder segmentBuilder = new();

            for (int index = 0; index < value.Length; index++)
            {
                if (TryHandleAnsiSequence(value, ref index, segmentBuilder))
                {
                    continue;
                }

                switch (value[index])
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
                        segmentBuilder.Append(value[index]);
                        break;
                }
            }

            FlushSegment(segmentBuilder);
        }

        public void WriteLine()
        {
            EnsureLine(CursorTop);
            _lines[CursorTop].HasTrailingNewLine = true;
            if (CursorTop < _maxCursorTop)
            {
                CursorTop++;
            }

            _cursorLeft = 0;
            EnsureLine(CursorTop);
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

        private void WriteSegment(string value)
        {
            if (value.Length == 0)
            {
                return;
            }

            EnsureLine(CursorTop);
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

        private bool TryHandleAnsiSequence(string value, ref int index, StringBuilder segmentBuilder)
        {
            if (value[index] != '\u001B' ||
                index + 1 >= value.Length ||
                value[index + 1] != '[')
            {
                return false;
            }

            int sequenceStart = index + 2;
            int sequenceEnd = sequenceStart;

            while (sequenceEnd < value.Length)
            {
                char sequenceCharacter = value[sequenceEnd];
                if (sequenceCharacter >= '@' && sequenceCharacter <= '~')
                {
                    break;
                }

                sequenceEnd++;
            }

            if (sequenceEnd >= value.Length)
            {
                index = value.Length - 1;
                return true;
            }

            FlushSegment(segmentBuilder);
            index = sequenceEnd;
            return true;
        }

        private void EnsureLine(int lineIndex)
        {
            while (_lines.Count <= lineIndex)
            {
                _lines.Add(new ConsoleLine());
            }
        }

        private sealed class ConsoleLine
        {
            public bool HasTrailingNewLine { get; set; }

            public StringBuilder Text { get; } = new();
        }
    }

    private sealed class RawCaptureConsoleTerminal : IConsoleTerminal
    {
        private readonly StringBuilder _rawOutput = new();
        private int _cursorLeft;

        public ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;

        public int CursorLeft => _cursorLeft;

        public int CursorTop { get; private set; }

        public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;

        public bool IsInputRedirected => false;

        public bool IsOutputRedirected => false;

        public bool KeyAvailable => false;

        public int WindowHeight => 30;

        public int WindowWidth => 120;

        public string RawOutput => _rawOutput.ToString();

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
            CursorTop = Math.Max(0, top);
            _cursorLeft = Math.Max(0, left);
            _rawOutput.Append($"<set:{left},{top}>");
        }

        public void Write(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            _rawOutput.Append(value);
            _cursorLeft += value.Length;
        }

        public void WriteLine()
        {
            _rawOutput.AppendLine();
            CursorTop++;
            _cursorLeft = 0;
        }

        public void WriteLine(string value)
        {
            Write(value);
            WriteLine();
        }
    }
}
