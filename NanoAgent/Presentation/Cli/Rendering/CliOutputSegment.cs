namespace NanoAgent.Presentation.Cli.Rendering;

internal sealed class CliOutputSegment
{
    public CliOutputSegment(string text, CliOutputStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        Style = style;
    }

    public CliOutputStyle Style { get; }

    public string Text { get; }
}
