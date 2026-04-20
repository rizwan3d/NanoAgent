namespace NanoAgent.ConsoleHost.Rendering;

internal sealed class CliRenderLine
{
    public CliRenderLine(
        IReadOnlyList<CliInlineSegment> segments,
        CliRenderLineKind kind = CliRenderLineKind.Normal)
        : this(segments, kind, null)
    {
    }

    public CliRenderLine(
        IReadOnlyList<IReadOnlyList<CliInlineSegment>> cells,
        CliRenderLineKind kind = CliRenderLineKind.Normal)
        : this(
            [new CliInlineSegment(string.Join(" | ", cells.Select(static cell => string.Concat(cell.Select(static segment => segment.Text)))))],
            kind,
            cells)
    {
    }

    private CliRenderLine(
        IReadOnlyList<CliInlineSegment> segments,
        CliRenderLineKind kind,
        IReadOnlyList<IReadOnlyList<CliInlineSegment>>? cells)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count == 0)
        {
            throw new ArgumentException(
                "Render lines must contain at least one segment.",
                nameof(segments));
        }

        Segments = segments;
        Kind = kind;
        Cells = cells;
    }

    public IReadOnlyList<IReadOnlyList<CliInlineSegment>>? Cells { get; }

    public CliRenderLineKind Kind { get; }

    public IReadOnlyList<CliInlineSegment> Segments { get; }
}
