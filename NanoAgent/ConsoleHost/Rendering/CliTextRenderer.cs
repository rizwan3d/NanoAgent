using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NanoAgent.ConsoleHost.Rendering;

internal sealed class CliTextRenderer : ICliTextRenderer
{
    private readonly ICliOutputTarget _outputTarget;
    private readonly IAnsiConsole _console;

    public CliTextRenderer(
        ICliOutputTarget outputTarget,
        IAnsiConsole console)
    {
        _outputTarget = outputTarget;
        _console = console;
    }

    public async Task RenderAsync(
        CliRenderDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (document.Kind == CliRenderMessageKind.Assistant)
        {
            await RenderAssistantAsync(document, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            RenderStatus(document);
        }
    }

    private async Task RenderAssistantAsync(
        CliRenderDocument document,
        CancellationToken cancellationToken)
    {
        _console.Write(new Markup("[bold aqua]assistant[/]"));
        _console.WriteLine();

        for (int index = 0; index < document.Blocks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (index > 0)
            {
                _console.WriteLine();
            }

            _console.Write(CreateRenderable(document.Blocks[index]));
        }

        _console.WriteLine();
    }

    private IRenderable CreateRenderable(CliRenderBlock block)
    {
        return block.Kind switch
        {
            CliRenderBlockKind.Heading => CreateHeading(block),
            CliRenderBlockKind.CodeBlock => CreateCodeBlock(block),
            CliRenderBlockKind.Diff => CreateDiffBlock(block),
            CliRenderBlockKind.Table => CreateTable(block),
            CliRenderBlockKind.List => CreateList(block),
            CliRenderBlockKind.Quote => CreateQuote(block),
            CliRenderBlockKind.Rule => new Rule { Style = new Style(Color.Grey) },
            CliRenderBlockKind.Alert => CreateAlert(block),
            _ => CreateParagraph(block)
        };
    }

    private void RenderStatus(CliRenderDocument document)
    {
        foreach (CliRenderBlock block in document.Blocks)
        {
            RenderAlertBlock(block, document.Kind);
        }
    }

    private static IRenderable CreateHeading(CliRenderBlock block)
    {
        string titleMarkup = BuildMarkup(block.Lines[0], block.HeadingLevel <= 2 ? "bold white" : "white");

        if (block.HeadingLevel <= 2)
        {
            return new Rule(titleMarkup)
            {
                Justification = Justify.Left,
                Style = block.HeadingLevel == 1
                    ? new Style(Color.Aqua, decoration: Decoration.Bold)
                    : new Style(Color.Blue)
            };
        }

        return new Markup(titleMarkup);
    }

    private static IRenderable CreateParagraph(CliRenderBlock block)
    {
        return CreateRows(block.Lines.Select(static line => (IRenderable)new Markup(BuildMarkup(line, "grey"))));
    }

    private static IRenderable CreateList(CliRenderBlock block)
    {
        IEnumerable<IRenderable> rows = block.Lines.Select((line, index) =>
        {
            string markerMarkup = block.IsOrderedList
                ? $"[aqua]{index + 1}.[/]"
                : $"[aqua]{'\u2022'}[/]";

            return (IRenderable)new Markup($"{markerMarkup} {BuildMarkup(line, "grey")}");
        });

        return CreateRows(rows);
    }

    private static IRenderable CreateQuote(CliRenderBlock block)
    {
        return CreateRows(block.Lines.Select(static line =>
            (IRenderable)new Markup($"[grey]{'\u2502'}[/] {BuildMarkup(line, "italic grey")}")));
    }

    private static IRenderable CreateCodeBlock(CliRenderBlock block)
    {
        return CreatePanel(
            CreateRows(block.Lines.Select(static line =>
                (IRenderable)new Text(GetRawLineText(line), new Style(Color.Grey)))),
            string.IsNullOrWhiteSpace(block.Language) ? "code" : block.Language!,
            "grey",
            new Style(Color.Grey),
            BoxBorder.Rounded);
    }

    private static IRenderable CreateDiffBlock(CliRenderBlock block)
    {
        return CreatePanel(
            CreateRows(block.Lines.Select(static line =>
                (IRenderable)new Text(
                    GetRawLineText(line),
                    line.Kind switch
                    {
                        CliRenderLineKind.DiffAddition => new Style(Color.Green),
                        CliRenderLineKind.DiffRemoval => new Style(Color.Red),
                        CliRenderLineKind.DiffHeader => new Style(Color.Aqua),
                        _ => new Style(Color.Grey)
                    }))),
            "diff",
            "aqua",
            new Style(Color.Aqua),
            BoxBorder.Square);
    }

    private static IRenderable CreateTable(CliRenderBlock block)
    {
        Table table = new()
        {
            Border = TableBorder.Rounded,
            BorderStyle = new Style(Color.Grey),
            Expand = true,
            ShowRowSeparators = false,
            ShowHeaders = block.HasHeaderRow
        };

        CliRenderLine? headerRow = block.HasHeaderRow
            ? block.Lines[0]
            : null;

        IReadOnlyList<IReadOnlyList<CliInlineSegment>> headerCells = headerRow?.Cells ??
            block.Lines[0].Cells ??
            [block.Lines[0].Segments];

        for (int columnIndex = 0; columnIndex < headerCells.Count; columnIndex++)
        {
            TableColumn column = new(CreateCellRenderable(
                headerCells[columnIndex],
                isHeader: true))
            {
                Alignment = MapAlignment(block, columnIndex)
            };

            table.AddColumn(column);
        }

        IEnumerable<CliRenderLine> dataRows = block.HasHeaderRow
            ? block.Lines.Skip(1)
            : block.Lines;

        foreach (CliRenderLine row in dataRows)
        {
            IReadOnlyList<IReadOnlyList<CliInlineSegment>> cells = row.Cells ?? [row.Segments];
            IRenderable[] renderables = Enumerable.Range(0, headerCells.Count)
                .Select(columnIndex => CreateCellRenderable(
                    columnIndex < cells.Count ? cells[columnIndex] : [],
                    isHeader: false))
                .ToArray();

            table.AddRow(renderables);
        }

        return table;
    }

    private static IRenderable CreateAlert(CliRenderBlock block)
    {
        return CreatePanel(
            CreateRows(block.Lines.Select(static line =>
                (IRenderable)new Markup(BuildMarkup(line, "yellow")))),
            "note",
            "yellow",
            new Style(Color.Yellow),
            BoxBorder.Rounded);
    }

    private static Panel CreatePanel(
        IRenderable body,
        string header,
        string headerStyleMarkup,
        Style borderStyle,
        BoxBorder border)
    {
        Panel panel = new(body)
        {
            Border = border,
            BorderStyle = borderStyle,
            Expand = true,
            Header = new PanelHeader(
                $"[{headerStyleMarkup}]{Markup.Escape(header)}[/]",
                Justify.Left)
        };

        return panel;
    }

    private static Rows CreateRows(IEnumerable<IRenderable> renderables)
    {
        return new Rows(renderables.ToArray());
    }

    private static IRenderable CreateCellRenderable(
        IReadOnlyList<CliInlineSegment> segments,
        bool isHeader)
    {
        return new Markup(BuildMarkup(
            segments,
            isHeader ? "bold white" : "grey"));
    }

    private static string BuildMarkup(
        CliRenderLine line,
        string baseStyle)
    {
        return BuildMarkup(line.Segments, baseStyle);
    }

    private static string BuildMarkup(
        IReadOnlyList<CliInlineSegment> segments,
        string baseStyle)
    {
        StringBuilder builder = new();

        foreach (CliInlineSegment segment in segments)
        {
            builder.Append(segment.Style switch
            {
                CliInlineStyle.Strong => WrapMarkup(segment.Text, "bold white"),
                CliInlineStyle.Emphasis => WrapMarkup(segment.Text, "italic aqua"),
                CliInlineStyle.Code => WrapMarkup(segment.Text, "yellow"),
                CliInlineStyle.Link => BuildLinkMarkup(segment),
                _ => WrapMarkup(segment.Text, baseStyle)
            });
        }

        return builder.Length == 0
            ? WrapMarkup(string.Empty, baseStyle)
            : builder.ToString();
    }

    private static Justify MapAlignment(
        CliRenderBlock block,
        int columnIndex)
    {
        if (columnIndex >= block.TableColumnAlignments.Count)
        {
            return Justify.Left;
        }

        return block.TableColumnAlignments[columnIndex] switch
        {
            CliTableColumnAlignment.Center => Justify.Center,
            CliTableColumnAlignment.Right => Justify.Right,
            _ => Justify.Left
        };
    }

    private void RenderAlertBlock(
        CliRenderBlock block,
        CliRenderMessageKind kind)
    {
        CliOutputStyle style = kind switch
        {
            CliRenderMessageKind.Error => CliOutputStyle.Error,
            CliRenderMessageKind.Warning => CliOutputStyle.Warning,
            _ => CliOutputStyle.Info
        };

        string prefix = kind switch
        {
            CliRenderMessageKind.Error => "[error] ",
            CliRenderMessageKind.Warning => "[warning] ",
            _ => "[info] "
        };

        for (int index = 0; index < block.Lines.Count; index++)
        {
            List<CliOutputSegment> outputSegments = [];
            if (index == 0)
            {
                outputSegments.Add(new CliOutputSegment(prefix, style));
            }
            else
            {
                outputSegments.Add(new CliOutputSegment(new string(' ', prefix.Length), style));
            }

            outputSegments.AddRange(MapLineSegments(block.Lines[index], style));
            _outputTarget.WriteLine(outputSegments);
        }
    }

    private static IReadOnlyList<CliOutputSegment> MapLineSegments(
        CliRenderLine line,
        CliOutputStyle baseStyle)
    {
        List<CliOutputSegment> outputSegments = [];

        foreach (CliInlineSegment segment in line.Segments)
        {
            outputSegments.Add(new CliOutputSegment(
                segment.Text,
                MapInlineStyle(segment.Style, baseStyle)));

            if (segment.Style == CliInlineStyle.Link &&
                !string.IsNullOrWhiteSpace(segment.Target))
            {
                outputSegments.Add(new CliOutputSegment(
                    $" ({segment.Target})",
                    CliOutputStyle.Muted));
            }
        }

        return outputSegments;
    }

    private static CliOutputStyle MapInlineStyle(
        CliInlineStyle style,
        CliOutputStyle baseStyle)
    {
        return style switch
        {
            CliInlineStyle.Code => CliOutputStyle.InlineCode,
            CliInlineStyle.Strong => CliOutputStyle.Strong,
            CliInlineStyle.Emphasis => CliOutputStyle.Emphasis,
            CliInlineStyle.Link => CliOutputStyle.Link,
            _ => baseStyle
        };
    }

    private static string BuildLinkMarkup(CliInlineSegment segment)
    {
        string labelMarkup = WrapMarkup(segment.Text, "underline aqua");

        if (string.IsNullOrWhiteSpace(segment.Target))
        {
            return labelMarkup;
        }

        return $"{labelMarkup}[grey] ({Markup.Escape(segment.Target)})[/]";
    }

    private static string WrapMarkup(string text, string style)
    {
        return $"[{style}]{Markup.Escape(text)}[/]";
    }

    private static string GetRawLineText(CliRenderLine line)
    {
        return string.Concat(line.Segments.Select(static segment => segment.Text));
    }
}
