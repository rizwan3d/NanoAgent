using System.Text;
using NanoAgent.Application.Exceptions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NanoAgent.Presentation.Cli.Terminal;

internal sealed class ConsolePromptInputReader : IConsolePromptInputReader
{
    private static readonly Style PlainTextEchoStyle = new(Color.White);
    private static readonly Style SecretMaskEchoStyle = new(Color.Yellow);

    private readonly IConsoleTerminal _terminal;
    private readonly IAnsiConsole _console;

    public ConsolePromptInputReader(
        IConsoleTerminal terminal,
        IAnsiConsole console)
    {
        _terminal = terminal;
        _console = console;
    }

    public Task<string> ReadLineAsync(
        string? defaultValue,
        ConsoleInputEchoMode echoMode,
        bool allowCancellation,
        CancellationToken cancellationToken)
    {
        StringBuilder builder = new();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConsoleKeyInfo keyInfo = _terminal.ReadKey(intercept: true);
            switch (keyInfo.Key)
            {
                case ConsoleKey.Enter:
                    _terminal.WriteLine();
                    if (builder.Length == 0 && defaultValue is not null)
                    {
                        return Task.FromResult(defaultValue);
                    }

                    return Task.FromResult(builder.ToString());

                case ConsoleKey.Backspace:
                    if (builder.Length > 0)
                    {
                        builder.Length--;
                        _terminal.Write("\b \b");
                    }

                    break;

                case ConsoleKey.Escape when allowCancellation:
                    _terminal.WriteLine();
                    throw new PromptCancelledException();

                default:
                    if (!char.IsControl(keyInfo.KeyChar))
                    {
                        builder.Append(keyInfo.KeyChar);
                        WriteEcho(keyInfo.KeyChar, echoMode);
                    }

                    break;
            }
        }
    }

    private void WriteEcho(char character, ConsoleInputEchoMode echoMode)
    {
        if (echoMode == ConsoleInputEchoMode.SecretMask)
        {
            _console.Write(new Text("*", SecretMaskEchoStyle));
            return;
        }

        _console.Write(new Text(character.ToString(), PlainTextEchoStyle));
    }
}
