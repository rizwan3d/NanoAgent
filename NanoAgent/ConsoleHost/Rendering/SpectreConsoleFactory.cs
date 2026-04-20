using System.Text;
using NanoAgent.ConsoleHost.Terminal;
using Spectre.Console;

namespace NanoAgent.ConsoleHost.Rendering;

internal static class SpectreConsoleFactory
{
    public static IAnsiConsole Create(IConsoleTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        bool supportsAnsi =
            !terminal.IsOutputRedirected &&
            !string.Equals(
                Environment.GetEnvironmentVariable("NO_COLOR"),
                "1",
                StringComparison.Ordinal);

        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = supportsAnsi ? AnsiSupport.Yes : AnsiSupport.No,
            Interactive = terminal.IsOutputRedirected
                ? InteractionSupport.No
                : InteractionSupport.Yes,
            Out = new TerminalAnsiConsoleOutput(terminal)
        });
    }

    private sealed class TerminalAnsiConsoleOutput : IAnsiConsoleOutput
    {
        private readonly IConsoleTerminal _terminal;
        private readonly TerminalTextWriter _writer;

        public TerminalAnsiConsoleOutput(IConsoleTerminal terminal)
        {
            _terminal = terminal;
            _writer = new TerminalTextWriter(terminal);
        }

        public int Height => _terminal.WindowHeight > 0 ? _terminal.WindowHeight : 24;

        public bool IsTerminal => !_terminal.IsOutputRedirected;

        public int Width => _terminal.WindowWidth > 0 ? _terminal.WindowWidth : 80;

        public TextWriter Writer => _writer;

        public void SetEncoding(Encoding encoding)
        {
            ArgumentNullException.ThrowIfNull(encoding);
            _writer.SetEncoding(encoding);
        }
    }

    private sealed class TerminalTextWriter : TextWriter
    {
        private readonly IConsoleTerminal _terminal;
        private Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public TerminalTextWriter(IConsoleTerminal terminal)
        {
            _terminal = terminal;
        }

        public override Encoding Encoding => _encoding;

        public void SetEncoding(Encoding encoding)
        {
            _encoding = encoding;
        }

        public override void Write(char value)
        {
            Write(value.ToString());
        }

        public override void Write(string? value)
        {
            WriteCore(value, appendNewLine: false);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            Write(new string(buffer, index, count));
        }

        public override void WriteLine()
        {
            _terminal.WriteLine();
        }

        public override void WriteLine(string? value)
        {
            WriteCore(value, appendNewLine: true);
        }

        private void WriteCore(string? value, bool appendNewLine)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (appendNewLine)
                {
                    _terminal.WriteLine();
                }

                return;
            }

            StringBuilder segmentBuilder = new();

            foreach (char character in value)
            {
                switch (character)
                {
                    case '\r':
                        FlushSegment(segmentBuilder);
                        TryMoveToLineStart();
                        break;

                    case '\n':
                        FlushSegment(segmentBuilder);
                        _terminal.WriteLine();
                        break;

                    default:
                        segmentBuilder.Append(character);
                        break;
                }
            }

            FlushSegment(segmentBuilder);

            if (appendNewLine)
            {
                _terminal.WriteLine();
            }
        }

        private void FlushSegment(StringBuilder segmentBuilder)
        {
            if (segmentBuilder.Length == 0)
            {
                return;
            }

            _terminal.Write(segmentBuilder.ToString());
            segmentBuilder.Clear();
        }

        private void TryMoveToLineStart()
        {
            try
            {
                _terminal.SetCursorPosition(0, _terminal.CursorTop);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            catch (IOException)
            {
            }
        }
    }
}
