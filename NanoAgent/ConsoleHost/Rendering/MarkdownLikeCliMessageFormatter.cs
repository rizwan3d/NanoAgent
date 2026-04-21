using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigTable = Markdig.Extensions.Tables.Table;

namespace NanoAgent.ConsoleHost.Rendering;

internal sealed class MarkdownLikeCliMessageFormatter : ICliMessageFormatter
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .Build();

    public CliRenderDocument Format(
        CliRenderMessageKind kind,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return kind == CliRenderMessageKind.Assistant
            ? new CliRenderDocument(kind, ParseBlocks(Markdown.Parse(message, MarkdownPipeline)))
            : new CliRenderDocument(kind, [CreateAlertBlock(message)]);
    }

    private static IReadOnlyList<CliRenderBlock> ParseBlocks(IEnumerable<Block> markdownBlocks)
    {
        List<CliRenderBlock> blocks = [];

        foreach (Block block in markdownBlocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    blocks.Add(new CliRenderBlock(
                        CliRenderBlockKind.Heading,
                        [CreateLine(heading.Inline)],
                        headingLevel: heading.Level));
                    break;

                case ParagraphBlock paragraph:
                    blocks.Add(new CliRenderBlock(
                        CliRenderBlockKind.Paragraph,
                        [CreateLine(paragraph.Inline)]));
                    break;

                case ListBlock list:
                    blocks.Add(CreateListBlock(list));
                    break;

                case QuoteBlock quote:
                    blocks.Add(CreateQuoteBlock(quote));
                    break;

                case ThematicBreakBlock:
                    blocks.Add(new CliRenderBlock(
                        CliRenderBlockKind.Rule,
                        [new CliRenderLine([new CliInlineSegment(string.Empty)])]));
                    break;

                case MarkdigTable table:
                    blocks.Add(CreateTableBlock(table));
                    break;

                case FencedCodeBlock fencedCode:
                    blocks.AddRange(CreateFencedCodeBlocks(fencedCode));
                    break;

                case CodeBlock code:
                    blocks.Add(CreateCodeBlock(code, language: null));
                    break;

                case ContainerBlock container:
                    blocks.AddRange(ParseBlocks(container));
                    break;
            }
        }

        return blocks.Count == 0
            ? [CreateParagraphBlock(string.Empty)]
            : blocks;
    }

    private static CliRenderBlock CreateAlertBlock(string message)
    {
        CliRenderLine[] lines = NormalizeLines(message)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => new CliRenderLine(ParseInlineSegments(line.Trim())))
            .ToArray();

        return new CliRenderBlock(
            CliRenderBlockKind.Alert,
            lines.Length == 0
                ? [new CliRenderLine([new CliInlineSegment(message.Trim())])]
                : lines);
    }

    private static CliRenderBlock CreateParagraphBlock(string text)
    {
        return new CliRenderBlock(
            CliRenderBlockKind.Paragraph,
            [new CliRenderLine(ParseInlineSegments(text.Trim()))]);
    }

    private static CliRenderBlock CreateListBlock(ListBlock list)
    {
        List<CliRenderLine> items = [];

        foreach (Block itemBlock in list)
        {
            if (itemBlock is not ListItemBlock item)
            {
                continue;
            }

            items.Add(CreateListItemLine(item));
        }

        return new CliRenderBlock(
            CliRenderBlockKind.List,
            items.Count == 0
                ? [new CliRenderLine([new CliInlineSegment(string.Empty)])]
                : items,
            isOrderedList: list.IsOrdered);
    }

    private static CliRenderLine CreateListItemLine(ListItemBlock item)
    {
        ParagraphBlock? paragraph = item.OfType<ParagraphBlock>().FirstOrDefault();
        return paragraph?.Inline is null
            ? new CliRenderLine(ParseInlineSegments(FlattenBlocks(item)))
            : CreateLine(paragraph.Inline);
    }

    private static CliRenderBlock CreateQuoteBlock(QuoteBlock quote)
    {
        string[] lines = NormalizeLines(FlattenBlocks(quote))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return new CliRenderBlock(
            CliRenderBlockKind.Quote,
            lines.Length == 0
                ? [new CliRenderLine([new CliInlineSegment(string.Empty)])]
                : lines.Select(static line => new CliRenderLine(ParseInlineSegments(line.Trim()))).ToArray());
    }

    private static IReadOnlyList<CliRenderBlock> CreateFencedCodeBlocks(FencedCodeBlock block)
    {
        string language = block.Info?.Trim() ?? string.Empty;
        string text = GetBlockText(block);

        if (IsMarkdownLanguage(language) && !string.IsNullOrWhiteSpace(text))
        {
            return ParseBlocks(Markdown.Parse(text, MarkdownPipeline));
        }

        return [IsDiffBlock(language, text)
            ? CreateDiffBlock(text)
            : CreateCodeBlock(block, language)];
    }

    private static CliRenderBlock CreateCodeBlock(
        LeafBlock block,
        string? language)
    {
        CliRenderLine[] lines = NormalizeLines(GetBlockText(block))
            .Select(static line => new CliRenderLine([new CliInlineSegment(line)]))
            .ToArray();

        return new CliRenderBlock(
            CliRenderBlockKind.CodeBlock,
            lines.Length == 0
                ? [new CliRenderLine([new CliInlineSegment(string.Empty)])]
                : lines,
            language);
    }

    private static CliRenderBlock CreateDiffBlock(string text)
    {
        CliRenderLine[] lines = NormalizeLines(text)
            .Select(static line => new CliRenderLine(
                [new CliInlineSegment(line)],
                GetDiffLineKind(line)))
            .ToArray();

        return new CliRenderBlock(
            CliRenderBlockKind.Diff,
            lines.Length == 0
                ? [new CliRenderLine([new CliInlineSegment(string.Empty)], CliRenderLineKind.DiffContext)]
                : lines,
            "diff");
    }

    private static CliRenderBlock CreateTableBlock(MarkdigTable table)
    {
        List<CliRenderLine> rows = [];
        foreach (Block rowBlock in table)
        {
            if (rowBlock is not TableRow row)
            {
                continue;
            }

            rows.Add(new CliRenderLine(row
                .OfType<TableCell>()
                .Select(CreateCellSegments)
                .ToArray()));
        }

        return new CliRenderBlock(
            CliRenderBlockKind.Table,
            rows.Count == 0
                ? [new CliRenderLine([new CliInlineSegment(string.Empty)])]
                : rows,
            hasHeaderRow: rows.Count > 0,
            tableColumnAlignments: table.ColumnDefinitions
                .Select(static column => column.Alignment switch
                {
                    TableColumnAlign.Center => CliTableColumnAlignment.Center,
                    TableColumnAlign.Right => CliTableColumnAlignment.Right,
                    _ => CliTableColumnAlignment.Left
                })
                .ToArray());
    }

    private static IReadOnlyList<CliInlineSegment> CreateCellSegments(TableCell cell)
    {
        ParagraphBlock? paragraph = cell.OfType<ParagraphBlock>().FirstOrDefault();
        return paragraph?.Inline is null
            ? ParseInlineSegments(FlattenBlocks(cell))
            : ParseInlineSegments(paragraph.Inline);
    }

    private static CliRenderLine CreateLine(ContainerInline? inline)
    {
        return new CliRenderLine(ParseInlineSegments(inline));
    }

    private static IReadOnlyList<CliInlineSegment> ParseInlineSegments(ContainerInline? inline)
    {
        if (inline is null)
        {
            return [new CliInlineSegment(string.Empty)];
        }

        List<CliInlineSegment> segments = [];
        AppendInlineSegments(inline, CliInlineStyle.Plain, segments);
        return MergePlainSegments(segments);
    }

    private static IReadOnlyList<CliInlineSegment> ParseInlineSegments(string text)
    {
        MarkdownDocument document = Markdown.Parse(text, MarkdownPipeline);
        ParagraphBlock? paragraph = document.OfType<ParagraphBlock>().FirstOrDefault();
        return paragraph?.Inline is null
            ? [new CliInlineSegment(text)]
            : ParseInlineSegments(paragraph.Inline);
    }

    private static void AppendInlineSegments(
        Inline inline,
        CliInlineStyle style,
        List<CliInlineSegment> segments)
    {
        switch (inline)
        {
            case LiteralInline literal:
                segments.Add(new CliInlineSegment(literal.Content.ToString(), style));
                break;

            case CodeInline code:
                segments.Add(new CliInlineSegment(code.Content, CliInlineStyle.Code));
                break;

            case LineBreakInline:
                segments.Add(new CliInlineSegment(Environment.NewLine, style));
                break;

            case LinkInline { IsImage: false } link:
                segments.Add(new CliInlineSegment(
                    FlattenInlines(link),
                    CliInlineStyle.Link,
                    link.Url));
                break;

            case EmphasisInline emphasis:
                AppendContainerInlineSegments(
                    emphasis,
                    emphasis.DelimiterCount >= 2 ? CliInlineStyle.Strong : CliInlineStyle.Emphasis,
                    segments);
                break;

            case ContainerInline container:
                AppendContainerInlineSegments(container, style, segments);
                break;
        }
    }

    private static void AppendContainerInlineSegments(
        ContainerInline inline,
        CliInlineStyle style,
        List<CliInlineSegment> segments)
    {
        for (Inline? child = inline.FirstChild; child is not null; child = child.NextSibling)
        {
            AppendInlineSegments(child, style, segments);
        }
    }

    private static string FlattenBlocks(ContainerBlock block)
    {
        return string.Join(
            Environment.NewLine,
            block.Select(FlattenBlock)
                .Where(static text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string FlattenBlock(Block block)
    {
        return block switch
        {
            LeafBlock { Inline: not null } leaf => FlattenInlines(leaf.Inline),
            LeafBlock leaf => GetBlockText(leaf),
            ContainerBlock container => FlattenBlocks(container),
            _ => string.Empty
        };
    }

    private static string FlattenInlines(ContainerInline inline)
    {
        List<CliInlineSegment> segments = [];
        AppendContainerInlineSegments(inline, CliInlineStyle.Plain, segments);
        return string.Concat(segments.Select(static segment => segment.Text));
    }

    private static string GetBlockText(LeafBlock block)
    {
        return block.Lines.ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n', '\r');
    }

    private static string[] NormalizeLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);
    }

    private static bool IsMarkdownLanguage(string language)
    {
        return string.Equals(language, "markdown", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(language, "md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiffBlock(
        string language,
        string text)
    {
        return string.Equals(language, "diff", StringComparison.OrdinalIgnoreCase) ||
               NormalizeLines(text).Any(static line => LooksLikeDiffLine(line));
    }

    private static bool LooksLikeDiffLine(string line)
    {
        return line.StartsWith("@@", StringComparison.Ordinal) ||
               line.StartsWith("diff ", StringComparison.Ordinal) ||
               line.StartsWith("index ", StringComparison.Ordinal) ||
               line.StartsWith("--- ", StringComparison.Ordinal) ||
               line.StartsWith("+++ ", StringComparison.Ordinal) ||
               line.StartsWith("+", StringComparison.Ordinal) ||
               line.StartsWith("-", StringComparison.Ordinal);
    }

    private static CliRenderLineKind GetDiffLineKind(string line)
    {
        if (line.StartsWith("+", StringComparison.Ordinal) &&
            !line.StartsWith("+++", StringComparison.Ordinal))
        {
            return CliRenderLineKind.DiffAddition;
        }

        if (line.StartsWith("-", StringComparison.Ordinal) &&
            !line.StartsWith("---", StringComparison.Ordinal))
        {
            return CliRenderLineKind.DiffRemoval;
        }

        return line.StartsWith("@@", StringComparison.Ordinal) ||
               line.StartsWith("diff ", StringComparison.Ordinal) ||
               line.StartsWith("index ", StringComparison.Ordinal) ||
               line.StartsWith("--- ", StringComparison.Ordinal) ||
               line.StartsWith("+++ ", StringComparison.Ordinal)
            ? CliRenderLineKind.DiffHeader
            : CliRenderLineKind.DiffContext;
    }

    private static IReadOnlyList<CliInlineSegment> MergePlainSegments(IReadOnlyList<CliInlineSegment> segments)
    {
        List<CliInlineSegment> mergedSegments = [];

        foreach (CliInlineSegment segment in segments)
        {
            if (mergedSegments.Count > 0 &&
                mergedSegments[^1].Style == CliInlineStyle.Plain &&
                mergedSegments[^1].Target is null &&
                segment.Style == CliInlineStyle.Plain &&
                segment.Target is null)
            {
                CliInlineSegment previousSegment = mergedSegments[^1];
                mergedSegments[^1] = new CliInlineSegment(previousSegment.Text + segment.Text);
                continue;
            }

            mergedSegments.Add(segment);
        }

        return mergedSegments.Count == 0
            ? [new CliInlineSegment(string.Empty)]
            : mergedSegments;
    }
}
