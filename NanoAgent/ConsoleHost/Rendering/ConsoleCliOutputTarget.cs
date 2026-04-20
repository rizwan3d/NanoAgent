using Spectre.Console;

namespace NanoAgent.ConsoleHost.Rendering;

internal sealed class ConsoleCliOutputTarget : ICliOutputTarget
{
    private readonly Terminal.IConsoleTerminal _terminal;
    private readonly IAnsiConsole _console;

    public ConsoleCliOutputTarget(
        Terminal.IConsoleTerminal terminal,
        IAnsiConsole console)
    {
        _terminal = terminal;
        _console = console;
    }

    public bool SupportsColor =>
        _console.Profile.Capabilities.Ansi;

    public void WriteLine()
    {
        _terminal.WriteLine();
    }

    public void WriteLine(IReadOnlyList<CliOutputSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count == 0)
        {
            _terminal.WriteLine();
            return;
        }

        if (!SupportsColor || _terminal.IsOutputRedirected)
        {
            string plainText = string.Concat(segments.Select(static segment => segment.Text));
            _terminal.WriteLine(plainText);
            return;
        }

        foreach (CliOutputSegment segment in segments)
        {
            _terminal.Write(BuildAnsiSequence(segment.Style));
            _terminal.Write(segment.Text);
            _terminal.Write("\u001b[0m");
        }

        _terminal.WriteLine();
    }

    private static string BuildAnsiSequence(CliOutputStyle style)
    {
        string colorCode = style switch
        {
            CliOutputStyle.AssistantLabel => "36",
            CliOutputStyle.AssistantText => "90",
            CliOutputStyle.Heading => "97",
            CliOutputStyle.Strong => "97;1",
            CliOutputStyle.Emphasis => "36;3",
            CliOutputStyle.InlineCode => "33",
            CliOutputStyle.CodeFence => "90",
            CliOutputStyle.CodeText => "90",
            CliOutputStyle.DiffAddition => "32",
            CliOutputStyle.DiffRemoval => "31",
            CliOutputStyle.DiffHeader => "36",
            CliOutputStyle.DiffContext => "90",
            CliOutputStyle.Warning => "33",
            CliOutputStyle.Error => "31",
            CliOutputStyle.Info => "36",
            CliOutputStyle.Muted => "90",
            CliOutputStyle.Link => "36;4",
            _ => "0"
        };

        return $"\u001b[{colorCode}m";
    }
}
