using System.Text;
using NanoAgent.ConsoleHost.Terminal;

namespace NanoAgent.Tests.ConsoleHost.TestDoubles;

internal sealed class FakeConsoleTerminal : IConsoleTerminal
{
    private readonly Queue<ConsoleKeyInfo> _keyQueue = new();
    private readonly Queue<string?> _lineQueue = new();
    private readonly List<ConsoleLine> _lines = [new ConsoleLine()];
    private int _cursorLeft;

    public ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;

    public int CursorTop { get; private set; }

    public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;

    public bool IsInputRedirected { get; set; }

    public bool IsOutputRedirected { get; set; }

    public string Output => BuildOutput();

    public int WindowHeight { get; set; } = 30;

    public int WindowWidth { get; set; } = 120;

    public void EnqueueKey(ConsoleKeyInfo keyInfo)
    {
        _keyQueue.Enqueue(keyInfo);
    }

    public void EnqueueLine(string? input)
    {
        _lineQueue.Enqueue(input);
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        if (_keyQueue.Count == 0)
        {
            throw new InvalidOperationException("No queued key input is available.");
        }

        return _keyQueue.Dequeue();
    }

    public string? ReadLine()
    {
        if (_lineQueue.Count == 0)
        {
            throw new InvalidOperationException("No queued line input is available.");
        }

        return _lineQueue.Dequeue();
    }

    public void ResetColor()
    {
    }

    public void SetCursorPosition(int left, int top)
    {
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

        string normalized = RemoveAnsiSequences(value)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        string[] segments = normalized.Split('\n', StringSplitOptions.None);
        for (int index = 0; index < segments.Length; index++)
        {
            WriteSegment(segments[index]);
            if (index < segments.Length - 1)
            {
                WriteLine();
            }
        }
    }

    public void WriteLine()
    {
        EnsureLine(CursorTop);
        _lines[CursorTop].HasTrailingNewLine = true;
        CursorTop++;
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

    private void EnsureLine(int lineIndex)
    {
        while (_lines.Count <= lineIndex)
        {
            _lines.Add(new ConsoleLine());
        }
    }

    private static string RemoveAnsiSequences(string value)
    {
        StringBuilder builder = new();
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (current == '\u001B' &&
                index + 1 < value.Length &&
                value[index + 1] == '[')
            {
                index += 2;
                while (index < value.Length)
                {
                    char sequenceCharacter = value[index];
                    if (sequenceCharacter >= '@' && sequenceCharacter <= '~')
                    {
                        break;
                    }

                    index++;
                }

                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private sealed class ConsoleLine
    {
        public bool HasTrailingNewLine { get; set; }

        public StringBuilder Text { get; } = new();
    }
}
