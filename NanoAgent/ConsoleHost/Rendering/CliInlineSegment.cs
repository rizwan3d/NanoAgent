namespace NanoAgent.ConsoleHost.Rendering;

internal sealed class CliInlineSegment
{
    public CliInlineSegment(
        string text,
        CliInlineStyle style = CliInlineStyle.Plain,
        string? target = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        Style = style;
        Target = string.IsNullOrWhiteSpace(target)
            ? null
            : target.Trim();
    }

    public CliInlineStyle Style { get; }

    public string? Target { get; }

    public string Text { get; }
}
