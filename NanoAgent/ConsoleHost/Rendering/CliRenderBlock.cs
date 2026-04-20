namespace NanoAgent.ConsoleHost.Rendering;

internal sealed class CliRenderBlock
{
    public CliRenderBlock(
        CliRenderBlockKind kind,
        IReadOnlyList<CliRenderLine> lines,
        string? language = null,
        int headingLevel = 0,
        bool isOrderedList = false,
        bool hasHeaderRow = false,
        IReadOnlyList<CliTableColumnAlignment>? tableColumnAlignments = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ArgumentException(
                "Render blocks must contain at least one line.",
                nameof(lines));
        }

        Kind = kind;
        Lines = lines;
        Language = string.IsNullOrWhiteSpace(language)
            ? null
            : language.Trim();
        HeadingLevel = headingLevel;
        IsOrderedList = isOrderedList;
        HasHeaderRow = hasHeaderRow;
        TableColumnAlignments = tableColumnAlignments ?? [];
    }

    public bool HasHeaderRow { get; }

    public int HeadingLevel { get; }

    public bool IsOrderedList { get; }

    public CliRenderBlockKind Kind { get; }

    public string? Language { get; }

    public IReadOnlyList<CliRenderLine> Lines { get; }

    public IReadOnlyList<CliTableColumnAlignment> TableColumnAlignments { get; }
}
