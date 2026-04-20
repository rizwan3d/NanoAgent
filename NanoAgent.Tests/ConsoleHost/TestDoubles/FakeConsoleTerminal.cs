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

    public int CursorLeft => _cursorLeft;

    public int CursorTop { get; private set; }

    public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;

    public bool IsInputRedirected { get; set; }

    public bool IsOutputRedirected { get; set; }

    public bool KeyAvailable => _keyQueue.Count > 0;

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

        char command = value[sequenceEnd];
        if (command == 'M')
        {
            string parameter = value.Substring(sequenceStart, sequenceEnd - sequenceStart);
            int deleteLineCount = int.TryParse(parameter, out int parsedDeleteLineCount) && parsedDeleteLineCount > 0
                ? parsedDeleteLineCount
                : 1;
            DeleteLines(deleteLineCount);
        }

        index = sequenceEnd;
        return true;
    }

    private void DeleteLines(int count)
    {
        if (count <= 0)
        {
            return;
        }

        EnsureLine(CursorTop);

        int linesToDelete = Math.Min(count, _lines.Count - CursorTop);
        if (linesToDelete <= 0)
        {
            return;
        }

        _lines.RemoveRange(CursorTop, linesToDelete);

        for (int index = 0; index < linesToDelete; index++)
        {
            _lines.Add(new ConsoleLine());
        }

        _cursorLeft = 0;
        EnsureLine(CursorTop);
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
