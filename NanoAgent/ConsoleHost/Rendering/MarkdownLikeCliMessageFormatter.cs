namespace NanoAgent.ConsoleHost.Rendering;

internal sealed class MarkdownLikeCliMessageFormatter : ICliMessageFormatter
{
    public CliRenderDocument Format(
        CliRenderMessageKind kind,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return kind == CliRenderMessageKind.Assistant
            ? new CliRenderDocument(kind, ParseAssistantMessage(message))
            : new CliRenderDocument(kind, [CreateAlertBlock(message)]);
    }

    private static IReadOnlyList<CliRenderBlock> ParseAssistantMessage(string message)
    {
        string[] lines = NormalizeLines(message);
        List<CliRenderBlock> blocks = [];
        List<string> paragraphLines = [];

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];

            if (IsFenceStart(line, out string? language))
            {
                FlushParagraph(blocks, paragraphLines);
                blocks.AddRange(ParseFencedBlocks(lines, ref index, language));
                continue;
            }

            if (TryParseHeading(line, out int headingLevel, out string? headingText))
            {
                FlushParagraph(blocks, paragraphLines);
                blocks.Add(new CliRenderBlock(
                    CliRenderBlockKind.Heading,
                    [new CliRenderLine(ParseInlineSegments(headingText!))],
                    headingLevel: headingLevel));
                continue;
            }

            if (TryParseStandaloneDiff(lines, ref index, out CliRenderBlock? diffBlock))
            {
                FlushParagraph(blocks, paragraphLines);
                blocks.Add(diffBlock!);
                continue;
            }

            if (TryParseTable(lines, ref index, out CliRenderBlock? tableBlock))
            {
                FlushParagraph(blocks, paragraphLines);
                blocks.Add(tableBlock!);
                continue;
            }

            if (IsHorizontalRule(line))
            {
                FlushParagraph(blocks, paragraphLines);
                blocks.Add(CreateRuleBlock());
                continue;
            }

            if (TryParseListItem(line, out bool isOrderedList, out string? listItemText))
            {
                FlushParagraph(blocks, paragraphLines);
                blocks.Add(ParseListBlock(lines, ref index, isOrderedList, listItemText!));
                continue;
            }

            if (TryParseQuoteLine(line, out string? quoteText))
            {
                FlushParagraph(blocks, paragraphLines);
                blocks.Add(ParseQuoteBlock(lines, ref index, quoteText!));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(blocks, paragraphLines);
                continue;
            }

            paragraphLines.Add(line);
        }

        FlushParagraph(blocks, paragraphLines);

        if (blocks.Count == 0)
        {
            blocks.Add(CreateParagraphBlock([message.Trim()]));
        }

        return blocks;
    }

    private static CliRenderBlock CreateAlertBlock(string message)
    {
        string[] lines = NormalizeLines(message)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length == 0)
        {
            lines = [message.Trim()];
        }

        return new CliRenderBlock(
            CliRenderBlockKind.Alert,
            lines.Select(static line => new CliRenderLine(ParseInlineSegments(line.Trim())))
                .ToArray());
    }

    private static void FlushParagraph(
        List<CliRenderBlock> blocks,
        List<string> paragraphLines)
    {
        if (paragraphLines.Count == 0)
        {
            return;
        }

        blocks.Add(CreateParagraphBlock(paragraphLines));
        paragraphLines.Clear();
    }

    private static CliRenderBlock CreateParagraphBlock(IReadOnlyList<string> paragraphLines)
    {
        return new CliRenderBlock(
            CliRenderBlockKind.Paragraph,
            paragraphLines
                .Select(static line => new CliRenderLine(ParseInlineSegments(line.Trim())))
                .ToArray());
    }

    private static IReadOnlyList<CliRenderBlock> ParseFencedBlocks(
        string[] lines,
        ref int index,
        string? language)
    {
        List<string> contentLines = [];

        index++;
        for (; index < lines.Length; index++)
        {
            if (lines[index].Trim() == "```")
            {
                break;
            }

            contentLines.Add(lines[index]);
        }

        bool isDiff = string.Equals(language, "diff", StringComparison.OrdinalIgnoreCase) ||
                      contentLines.Any(static line => LooksLikeStandaloneDiffContent(line));

        if (IsMarkdownLanguage(language) && contentLines.Count > 0)
        {
            return ParseAssistantMessage(string.Join('\n', contentLines));
        }

        return isDiff
            ? [CreateDiffBlock(contentLines)]
            : [CreateCodeBlock(contentLines, language)];
    }

    private static CliRenderBlock ParseListBlock(
        string[] lines,
        ref int index,
        bool isOrderedList,
        string firstItemText)
    {
        List<CliRenderLine> items =
        [
            new CliRenderLine(ParseInlineSegments(firstItemText))
        ];

        for (int nextIndex = index + 1; nextIndex < lines.Length; nextIndex++)
        {
            if (!TryParseListItem(lines[nextIndex], out bool currentIsOrderedList, out string? itemText) ||
                currentIsOrderedList != isOrderedList)
            {
                break;
            }

            items.Add(new CliRenderLine(ParseInlineSegments(itemText!)));
            index = nextIndex;
        }

        return new CliRenderBlock(
            CliRenderBlockKind.List,
            items,
            isOrderedList: isOrderedList);
    }

    private static CliRenderBlock ParseQuoteBlock(
        string[] lines,
        ref int index,
        string firstQuoteLine)
    {
        List<CliRenderLine> quoteLines =
        [
            new CliRenderLine(ParseInlineSegments(firstQuoteLine))
        ];

        for (int nextIndex = index + 1; nextIndex < lines.Length; nextIndex++)
        {
            if (!TryParseQuoteLine(lines[nextIndex], out string? quoteText))
            {
                break;
            }

            quoteLines.Add(new CliRenderLine(ParseInlineSegments(quoteText!)));
            index = nextIndex;
        }

        return new CliRenderBlock(
            CliRenderBlockKind.Quote,
            quoteLines);
    }

    private static bool TryParseStandaloneDiff(
        string[] lines,
        ref int index,
        out CliRenderBlock? block)
    {
        if (!LooksLikeDiffHeader(lines[index]))
        {
            block = null;
            return false;
        }

        List<string> diffLines = [];

        for (; index < lines.Length; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                index--;
                break;
            }

            if (!LooksLikeStandaloneDiffContent(line))
            {
                index--;
                break;
            }

            diffLines.Add(line);
        }

        block = CreateDiffBlock(diffLines);
        return true;
    }

    private static bool TryParseTable(
        string[] lines,
        ref int index,
        out CliRenderBlock? block)
    {
        if (index + 1 >= lines.Length ||
            !TrySplitTableRow(lines[index], out IReadOnlyList<string>? headerCells) ||
            headerCells is null ||
            !TryParseTableSeparator(lines[index + 1], headerCells.Count, out IReadOnlyList<CliTableColumnAlignment>? alignments) ||
            alignments is null)
        {
            block = null;
            return false;
        }

        List<CliRenderLine> rows =
        [
            CreateTableRow(headerCells)
        ];

        int lastConsumedLineIndex = index + 1;
        for (int nextIndex = index + 2; nextIndex < lines.Length; nextIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[nextIndex]))
            {
                break;
            }

            if (!TrySplitTableRow(lines[nextIndex], out IReadOnlyList<string>? rowCells) ||
                rowCells is null ||
                rowCells.Count != headerCells.Count)
            {
                break;
            }

            rows.Add(CreateTableRow(rowCells));
            lastConsumedLineIndex = nextIndex;
        }

        index = lastConsumedLineIndex;
        block = new CliRenderBlock(
            CliRenderBlockKind.Table,
            rows,
            hasHeaderRow: true,
            tableColumnAlignments: alignments);
        return true;
    }

    private static CliRenderBlock CreateCodeBlock(
        IReadOnlyList<string> lines,
        string? language)
    {
        IReadOnlyList<CliRenderLine> renderLines = lines.Count == 0
            ? [new CliRenderLine([new CliInlineSegment(string.Empty)])]
            : lines.Select(static line => new CliRenderLine([new CliInlineSegment(line)]))
                .ToArray();

        return new CliRenderBlock(
            CliRenderBlockKind.CodeBlock,
            renderLines,
            language);
    }

    private static CliRenderBlock CreateDiffBlock(IReadOnlyList<string> lines)
    {
        IReadOnlyList<CliRenderLine> renderLines = lines.Count == 0
            ? [new CliRenderLine([new CliInlineSegment(string.Empty)], CliRenderLineKind.DiffContext)]
            : lines.Select(static line => new CliRenderLine(
                    [new CliInlineSegment(line)],
                    GetDiffLineKind(line)))
                .ToArray();

        return new CliRenderBlock(
            CliRenderBlockKind.Diff,
            renderLines,
            "diff");
    }

    private static CliRenderLine CreateTableRow(IReadOnlyList<string> cells)
    {
        return new CliRenderLine(cells.Select(static cell => ParseInlineSegments(cell)).ToArray());
    }

    private static CliRenderBlock CreateRuleBlock()
    {
        return new CliRenderBlock(
            CliRenderBlockKind.Rule,
            [new CliRenderLine([new CliInlineSegment(string.Empty)])]);
    }

    private static bool IsFenceStart(
        string line,
        out string? language)
    {
        string trimmed = line.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            language = null;
            return false;
        }

        language = trimmed.Length == 3
            ? null
            : trimmed[3..].Trim();

        return true;
    }

    private static bool IsMarkdownLanguage(string? language)
    {
        return string.Equals(language, "markdown", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(language, "md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseHeading(
        string line,
        out int headingLevel,
        out string? headingText)
    {
        string trimmed = line.Trim();
        headingLevel = 0;
        headingText = null;

        if (trimmed.Length < 2 || trimmed[0] != '#')
        {
            return false;
        }

        while (headingLevel < trimmed.Length &&
               trimmed[headingLevel] == '#')
        {
            headingLevel++;
        }

        if (headingLevel == 0 ||
            headingLevel > 6 ||
            headingLevel >= trimmed.Length ||
            trimmed[headingLevel] != ' ')
        {
            return false;
        }

        headingText = trimmed[(headingLevel + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(headingText);
    }

    private static bool TryParseListItem(
        string line,
        out bool isOrderedList,
        out string? itemText)
    {
        string trimmed = line.TrimStart();
        isOrderedList = false;
        itemText = null;

        if (trimmed.Length < 3)
        {
            return false;
        }

        if ((trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+') &&
            trimmed[1] == ' ')
        {
            itemText = trimmed[2..].Trim();
            return !string.IsNullOrWhiteSpace(itemText);
        }

        int markerLength = 0;
        while (markerLength < trimmed.Length && char.IsDigit(trimmed[markerLength]))
        {
            markerLength++;
        }

        if (markerLength == 0 ||
            markerLength + 1 >= trimmed.Length ||
            trimmed[markerLength] != '.' ||
            trimmed[markerLength + 1] != ' ')
        {
            return false;
        }

        isOrderedList = true;
        itemText = trimmed[(markerLength + 2)..].Trim();
        return !string.IsNullOrWhiteSpace(itemText);
    }

    private static bool TryParseQuoteLine(
        string line,
        out string? quoteText)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith('>'))
        {
            quoteText = null;
            return false;
        }

        quoteText = trimmed.Length == 1
            ? string.Empty
            : trimmed[1] == ' '
                ? trimmed[2..].Trim()
                : trimmed[1..].Trim();

        return true;
    }

    private static bool IsHorizontalRule(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Length >= 3 &&
               trimmed.All(character => character == trimmed[0]) &&
               (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '_');
    }

    private static bool TrySplitTableRow(
        string line,
        out IReadOnlyList<string>? cells)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || !trimmed.Contains('|', StringComparison.Ordinal))
        {
            cells = null;
            return false;
        }

        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|'))
        {
            trimmed = trimmed[..^1];
        }

        string[] rawCells = trimmed
            .Split('|')
            .Select(static cell => cell.Trim())
            .ToArray();

        if (rawCells.Length == 0 || rawCells.All(string.IsNullOrWhiteSpace))
        {
            cells = null;
            return false;
        }

        cells = rawCells;
        return true;
    }

    private static bool TryParseTableSeparator(
        string line,
        int expectedColumnCount,
        out IReadOnlyList<CliTableColumnAlignment>? alignments)
    {
        if (!TrySplitTableRow(line, out IReadOnlyList<string>? cells) ||
            cells is null ||
            cells.Count != expectedColumnCount)
        {
            alignments = null;
            return false;
        }

        List<CliTableColumnAlignment> parsedAlignments = [];
        foreach (string cell in cells)
        {
            string normalized = cell.Replace(" ", string.Empty, StringComparison.Ordinal);
            int dashCount = normalized.Count(static character => character == '-');

            if (dashCount < 3 ||
                normalized.Any(static character => character is not ('-' or ':')))
            {
                alignments = null;
                return false;
            }

            bool hasLeadingColon = normalized.StartsWith(':');
            bool hasTrailingColon = normalized.EndsWith(':');

            parsedAlignments.Add(hasLeadingColon && hasTrailingColon
                ? CliTableColumnAlignment.Center
                : hasTrailingColon
                    ? CliTableColumnAlignment.Right
                    : CliTableColumnAlignment.Left);
        }

        alignments = parsedAlignments;
        return true;
    }

    private static bool LooksLikeDiffHeader(string line)
    {
        return line.StartsWith("@@", StringComparison.Ordinal) ||
               line.StartsWith("diff ", StringComparison.Ordinal) ||
               line.StartsWith("index ", StringComparison.Ordinal) ||
               line.StartsWith("--- ", StringComparison.Ordinal) ||
               line.StartsWith("+++ ", StringComparison.Ordinal);
    }

    private static bool LooksLikeStandaloneDiffContent(string line)
    {
        return LooksLikeDiffHeader(line) ||
               line.StartsWith("+", StringComparison.Ordinal) ||
               line.StartsWith("-", StringComparison.Ordinal) ||
               line.StartsWith(" ", StringComparison.Ordinal);
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

        if (LooksLikeDiffHeader(line))
        {
            return CliRenderLineKind.DiffHeader;
        }

        return CliRenderLineKind.DiffContext;
    }

    private static string[] NormalizeLines(string message)
    {
        return message
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
    }

    private static IReadOnlyList<CliInlineSegment> ParseInlineSegments(string text)
    {
        List<CliInlineSegment> segments = [];
        int index = 0;

        while (index < text.Length)
        {
            if (TryConsumeLink(text, index, out CliInlineSegment? linkSegment, out int linkLength))
            {
                segments.Add(linkSegment!);
                index += linkLength;
                continue;
            }

            if (TryConsumeDelimited(text, index, "**", CliInlineStyle.Strong, out CliInlineSegment? strongSegment, out int strongLength))
            {
                segments.Add(strongSegment!);
                index += strongLength;
                continue;
            }

            if (TryConsumeDelimited(text, index, "`", CliInlineStyle.Code, out CliInlineSegment? codeSegment, out int codeLength))
            {
                segments.Add(codeSegment!);
                index += codeLength;
                continue;
            }

            if (TryConsumeDelimited(text, index, "*", CliInlineStyle.Emphasis, out CliInlineSegment? emphasisSegment, out int emphasisLength))
            {
                segments.Add(emphasisSegment!);
                index += emphasisLength;
                continue;
            }

            int nextMarker = FindNextMarker(text, index);
            if (nextMarker == index)
            {
                segments.Add(new CliInlineSegment(text[index].ToString()));
                index++;
                continue;
            }

            string chunk = nextMarker < 0
                ? text[index..]
                : text[index..nextMarker];

            if (!string.IsNullOrEmpty(chunk))
            {
                segments.Add(new CliInlineSegment(chunk));
            }

            index = nextMarker < 0
                ? text.Length
                : nextMarker;
        }

        if (segments.Count == 0)
        {
            segments.Add(new CliInlineSegment(text));
        }

        return MergePlainSegments(segments);
    }

    private static bool TryConsumeLink(
        string text,
        int startIndex,
        out CliInlineSegment? segment,
        out int consumedLength)
    {
        if (startIndex >= text.Length || text[startIndex] != '[')
        {
            segment = null;
            consumedLength = 0;
            return false;
        }

        int closingLabelIndex = text.IndexOf(']', startIndex + 1);
        if (closingLabelIndex <= startIndex + 1 ||
            closingLabelIndex + 2 >= text.Length ||
            text[closingLabelIndex + 1] != '(')
        {
            segment = null;
            consumedLength = 0;
            return false;
        }

        int closingTargetIndex = text.IndexOf(')', closingLabelIndex + 2);
        if (closingTargetIndex <= closingLabelIndex + 2)
        {
            segment = null;
            consumedLength = 0;
            return false;
        }

        string label = text[(startIndex + 1)..closingLabelIndex];
        string target = text[(closingLabelIndex + 2)..closingTargetIndex].Trim();
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(target))
        {
            segment = null;
            consumedLength = 0;
            return false;
        }

        segment = new CliInlineSegment(label, CliInlineStyle.Link, target);
        consumedLength = (closingTargetIndex + 1) - startIndex;
        return true;
    }

    private static bool TryConsumeDelimited(
        string text,
        int startIndex,
        string delimiter,
        CliInlineStyle style,
        out CliInlineSegment? segment,
        out int consumedLength)
    {
        if (!text.AsSpan(startIndex).StartsWith(delimiter, StringComparison.Ordinal))
        {
            segment = null;
            consumedLength = 0;
            return false;
        }

        int innerStart = startIndex + delimiter.Length;
        int closingIndex = text.IndexOf(delimiter, innerStart, StringComparison.Ordinal);
        if (closingIndex <= innerStart)
        {
            segment = null;
            consumedLength = 0;
            return false;
        }

        string innerText = text[innerStart..closingIndex];
        if (string.IsNullOrEmpty(innerText))
        {
            segment = null;
            consumedLength = 0;
            return false;
        }

        segment = new CliInlineSegment(innerText, style);
        consumedLength = (closingIndex + delimiter.Length) - startIndex;
        return true;
    }

    private static int FindNextMarker(string text, int startIndex)
    {
        int nextStrong = text.IndexOf("**", startIndex, StringComparison.Ordinal);
        int nextCode = text.IndexOf('`', startIndex);
        int nextEmphasis = text.IndexOf('*', startIndex);
        int nextLink = text.IndexOf('[', startIndex);

        int[] candidates =
        [
            nextStrong,
            nextCode,
            nextEmphasis,
            nextLink
        ];

        return candidates
            .Where(static index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
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
            }
            else
            {
                mergedSegments.Add(segment);
            }
        }

        return mergedSegments;
    }
}
