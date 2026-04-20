using Spectre.Console;

namespace NanoAgent.ConsoleHost.Rendering;

internal sealed class ConsoleCliOutputTarget : ICliOutputTarget
{
    private readonly IAnsiConsole _console;

    public ConsoleCliOutputTarget(IAnsiConsole console)
    {
        _console = console;
    }

    public bool SupportsColor =>
        _console.Profile.Capabilities.Ansi;

    public void WriteLine()
    {
        _console.WriteLine();
    }

    public void WriteLine(IReadOnlyList<CliOutputSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count == 0)
        {
            _console.WriteLine();
            return;
        }

        if (!SupportsColor)
        {
            string plainText = string.Concat(segments.Select(static segment => segment.Text));
            _console.WriteLine(plainText);
            return;
        }

        foreach (CliOutputSegment segment in segments)
        {
            _console.Write(segment.Text, MapStyle(segment.Style));
        }

        _console.WriteLine();
    }

    private static Style MapStyle(CliOutputStyle style)
    {
        return style switch
        {
            CliOutputStyle.AssistantLabel => new Style(Color.Aqua),
            CliOutputStyle.AssistantText => new Style(Color.Grey),
            CliOutputStyle.Heading => new Style(Color.White),
            CliOutputStyle.Strong => new Style(Color.White, decoration: Decoration.Bold),
            CliOutputStyle.Emphasis => new Style(Color.Aqua, decoration: Decoration.Italic),
            CliOutputStyle.InlineCode => new Style(Color.Yellow),
            CliOutputStyle.CodeFence => new Style(Color.Grey),
            CliOutputStyle.CodeText => new Style(Color.Grey),
            CliOutputStyle.DiffAddition => new Style(Color.Green),
            CliOutputStyle.DiffRemoval => new Style(Color.Red),
            CliOutputStyle.DiffHeader => new Style(Color.Aqua),
            CliOutputStyle.DiffContext => new Style(Color.Grey),
            CliOutputStyle.Warning => new Style(Color.Yellow),
            CliOutputStyle.Error => new Style(Color.Red),
            CliOutputStyle.Info => new Style(Color.Aqua),
            CliOutputStyle.Muted => new Style(Color.Grey),
            CliOutputStyle.Link => new Style(Color.Aqua, decoration: Decoration.Underline),
            _ => Style.Plain
        };
    }
}
