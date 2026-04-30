using Spectre.Console;
using System.Text;

namespace NanoAgent.CLI;

public static partial class Program
{
    private static void AddMarkdownTextLine(
        List<ConversationLine> lines,
        string rawLine,
        ref bool firstLine,
        string roleName,
        string roleColor,
        Role role,
        int contentWidth)
    {
        string trimmedLine = rawLine.Trim();

        if (role == Role.System &&
            TryGetToolOutputLineStyle(rawLine, out string toolOutputLineStyle))
        {
            AddWrappedMarkdownLine(
                lines,
                rawLine,
                toolOutputLineStyle,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                ref firstLine,
                roleName,
                roleColor,
                contentWidth);
            return;
        }

        if (TryGetMarkdownHeading(trimmedLine, out int headingLevel, out string headingText))
        {
            AddWrappedMarkdownLine(
                lines,
                headingText,
                GetMarkdownHeadingStyle(headingLevel),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                ref firstLine,
                roleName,
                roleColor,
                contentWidth);
            return;
        }

        if (TryGetMarkdownBullet(trimmedLine, out string bulletMarkup, out string bulletPlain, out string bulletText))
        {
            AddWrappedMarkdownLine(
                lines,
                bulletText,
                string.Empty,
                bulletMarkup,
                bulletPlain,
                new string(' ', bulletPlain.Length),
                new string(' ', bulletPlain.Length),
                ref firstLine,
                roleName,
                roleColor,
                contentWidth);
            return;
        }

        if (trimmedLine.StartsWith('>'))
        {
            string quoteText = trimmedLine[1..].TrimStart();

            AddWrappedMarkdownLine(
                lines,
                quoteText,
                "italic grey",
                "[grey]│[/] ",
                "│ ",
                "[grey]│[/] ",
                "│ ",
                ref firstLine,
                roleName,
                roleColor,
                contentWidth);
            return;
        }

        AddWrappedMarkdownLine(
            lines,
            rawLine,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            ref firstLine,
            roleName,
            roleColor,
            contentWidth);
    }

    private static void AddWrappedMarkdownLine(
        List<ConversationLine> lines,
        string text,
        string lineStyle,
        string firstContentPrefixMarkup,
        string firstContentPrefixPlain,
        string continuationContentPrefixMarkup,
        string continuationContentPrefixPlain,
        ref bool firstLine,
        string roleName,
        string roleColor,
        int contentWidth)
    {
        int firstMessagePrefixLength = firstLine ? roleName.Length + 2 : 5;
        int firstLineLength = Math.Max(1, contentWidth - firstMessagePrefixLength - firstContentPrefixPlain.Length);
        int continuationLineLength = Math.Max(1, contentWidth - 5 - continuationContentPrefixPlain.Length);
        List<MarkdownFragment> fragments = ParseInlineMarkdown(text);
        List<List<MarkdownFragment>> wrappedLines = WrapMarkdownFragments(
            fragments,
            firstLineLength,
            continuationLineLength);

        for (int index = 0; index < wrappedLines.Count; index++)
        {
            InlineRenderResult renderResult = RenderMarkdownFragments(wrappedLines[index], lineStyle);
            bool isFirstWrappedLine = index == 0;
            AddConversationContentLine(
                lines,
                (isFirstWrappedLine ? firstContentPrefixMarkup : continuationContentPrefixMarkup) + renderResult.Markup,
                (isFirstWrappedLine ? firstContentPrefixPlain : continuationContentPrefixPlain) + renderResult.Plain,
                ref firstLine,
                roleName,
                roleColor);
        }
    }

    private static void AddMarkdownTableLines(
        List<ConversationLine> lines,
        IReadOnlyList<string[]> rows,
        ref bool firstLine,
        string roleName,
        string roleColor,
        int contentWidth)
    {
        int columnCount = rows.Max(row => row.Length);
        int firstMessagePrefixLength = firstLine ? roleName.Length + 2 : 5;
        int tableContentWidth = Math.Max(
            1,
            Math.Min(contentWidth - firstMessagePrefixLength, contentWidth - 5));

        if (tableContentWidth < columnCount * 2 + 1)
        {
            foreach (string[] row in rows)
            {
                AddWrappedMarkdownLine(
                    lines,
                    string.Join(" | ", row),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    ref firstLine,
                    roleName,
                    roleColor,
                    contentWidth);
            }

            return;
        }

        bool compact = tableContentWidth < (columnCount * 4) + 1;
        int[] columnWidths = CalculateMarkdownTableColumnWidths(
            rows,
            columnCount,
            tableContentWidth,
            compact);

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            bool isHeader = rowIndex == 0;
            AddRenderedMarkdownTableRow(
                lines,
                rows[rowIndex],
                columnWidths,
                isHeader ? "bold deepskyblue1" : string.Empty,
                compact,
                ref firstLine,
                roleName,
                roleColor);

            if (isHeader)
            {
                AddMarkdownTableSeparatorLine(
                    lines,
                    columnWidths,
                    compact,
                    ref firstLine,
                    roleName,
                    roleColor);
            }
        }
    }

    private static void AddRenderedMarkdownTableRow(
        List<ConversationLine> lines,
        string[] row,
        IReadOnlyList<int> columnWidths,
        string cellLineStyle,
        bool compact,
        ref bool firstLine,
        string roleName,
        string roleColor)
    {
        List<List<InlineRenderResult>> renderedCells = [];

        for (int columnIndex = 0; columnIndex < columnWidths.Count; columnIndex++)
        {
            string cellText = columnIndex < row.Length
                ? row[columnIndex]
                : string.Empty;

            renderedCells.Add(RenderMarkdownTableCell(cellText, columnWidths[columnIndex], cellLineStyle));
        }

        int visualLineCount = renderedCells.Max(cell => cell.Count);

        for (int visualLineIndex = 0; visualLineIndex < visualLineCount; visualLineIndex++)
        {
            StringBuilder markup = new();
            StringBuilder plain = new();

            AppendMarkdownTableBorder(markup, plain);

            for (int columnIndex = 0; columnIndex < columnWidths.Count; columnIndex++)
            {
                if (!compact)
                {
                    markup.Append(' ');
                    plain.Append(' ');
                }

                InlineRenderResult cellLine = visualLineIndex < renderedCells[columnIndex].Count
                    ? renderedCells[columnIndex][visualLineIndex]
                    : new InlineRenderResult(string.Empty, string.Empty);
                int padding = Math.Max(0, columnWidths[columnIndex] - cellLine.Plain.Length);

                markup.Append(cellLine.Markup);
                markup.Append(new string(' ', padding));
                plain.Append(cellLine.Plain);
                plain.Append(new string(' ', padding));

                if (!compact)
                {
                    markup.Append(' ');
                    plain.Append(' ');
                }

                AppendMarkdownTableBorder(markup, plain);
            }

            AddConversationContentLine(
                lines,
                markup.ToString(),
                plain.ToString(),
                ref firstLine,
                roleName,
                roleColor);
        }
    }

    private static void AddMarkdownTableSeparatorLine(
        List<ConversationLine> lines,
        IReadOnlyList<int> columnWidths,
        bool compact,
        ref bool firstLine,
        string roleName,
        string roleColor)
    {
        StringBuilder markup = new();
        StringBuilder plain = new();

        AppendMarkdownTableBorder(markup, plain);

        foreach (int columnWidth in columnWidths)
        {
            if (!compact)
            {
                markup.Append(' ');
                plain.Append(' ');
            }

            string dashes = new('-', Math.Max(1, columnWidth));
            markup.Append("[grey]").Append(dashes).Append("[/]");
            plain.Append(dashes);

            if (!compact)
            {
                markup.Append(' ');
                plain.Append(' ');
            }

            AppendMarkdownTableBorder(markup, plain);
        }

        AddConversationContentLine(
            lines,
            markup.ToString(),
            plain.ToString(),
            ref firstLine,
            roleName,
            roleColor);
    }

    private static void AddConversationContentLine(
        List<ConversationLine> lines,
        string contentMarkup,
        string contentPlain,
        ref bool firstLine,
        string roleName,
        string roleColor)
    {
        string prefixPlain = firstLine ? $"{roleName}: " : "     ";
        string prefixMarkup = firstLine ? $"[{roleColor}]{roleName}:[/] " : "     ";

        lines.Add(new ConversationLine(
            prefixMarkup + contentMarkup,
            prefixPlain + contentPlain));

        firstLine = false;
    }

    private static bool TryGetMarkdownHeading(
        string trimmedLine,
        out int level,
        out string text)
    {
        level = 0;
        text = string.Empty;

        while (level < trimmedLine.Length &&
            level < 6 &&
            trimmedLine[level] == '#')
        {
            level++;
        }

        if (level == 0 ||
            level >= trimmedLine.Length ||
            !char.IsWhiteSpace(trimmedLine[level]))
        {
            level = 0;
            return false;
        }

        text = trimmedLine[(level + 1)..].Trim();
        return text.Length > 0;
    }

    private static string GetMarkdownHeadingStyle(int level)
    {
        return level switch
        {
            1 => "bold deepskyblue1",
            2 => "bold green",
            _ => "bold yellow"
        };
    }

    private static bool TryGetToolOutputLineStyle(
        string rawLine,
        out string style)
    {
        style = string.Empty;
        string trimmedStart = rawLine.TrimStart();

        if (trimmedStart.StartsWith("Running ", StringComparison.Ordinal))
        {
            style = "bold yellow";
            return true;
        }

        if (trimmedStart.StartsWith("Plan progress:", StringComparison.Ordinal))
        {
            style = "bold aqua";
            return true;
        }

        if (trimmedStart.StartsWith("✓ ", StringComparison.Ordinal))
        {
            style = "green";
            return true;
        }

        if (trimmedStart.StartsWith("☐ ", StringComparison.Ordinal))
        {
            style = "yellow";
            return true;
        }

        if (trimmedStart.StartsWith("• ", StringComparison.Ordinal))
        {
            style = GetToolOutputHeaderStyle(trimmedStart);
            return true;
        }

        if (trimmedStart.StartsWith("…", StringComparison.Ordinal) ||
            trimmedStart.StartsWith("...", StringComparison.Ordinal))
        {
            style = "grey";
            return true;
        }

        if (trimmedStart.StartsWith("- ", StringComparison.Ordinal))
        {
            style = trimmedStart.Contains("stderr:", StringComparison.OrdinalIgnoreCase)
                ? "bold red"
                : trimmedStart.Contains("stdout:", StringComparison.OrdinalIgnoreCase)
                    ? "bold deepskyblue1"
                    : "aqua";
            return true;
        }

        if (TryGetDiffPreviewLineStyle(trimmedStart, out style))
        {
            return true;
        }

        if (rawLine.StartsWith("    ", StringComparison.Ordinal))
        {
            style = "grey";
            return true;
        }

        return false;
    }

    private static string GetToolOutputHeaderStyle(string trimmedStart)
    {
        if (trimmedStart.StartsWith("• Edited ", StringComparison.Ordinal))
        {
            return "bold green";
        }

        if (trimmedStart.StartsWith("• Ran ", StringComparison.Ordinal))
        {
            return "bold deepskyblue1";
        }

        if (trimmedStart.StartsWith("• Read ", StringComparison.Ordinal))
        {
            return "bold aqua";
        }

        if (trimmedStart.StartsWith("• Previewed ", StringComparison.Ordinal))
        {
            return "bold cyan";
        }

        if (trimmedStart.StartsWith("• Listed ", StringComparison.Ordinal))
        {
            return "bold blue";
        }

        if (trimmedStart.StartsWith("• Searched ", StringComparison.Ordinal) ||
            trimmedStart.StartsWith("• Found ", StringComparison.Ordinal))
        {
            return "bold yellow";
        }

        if (trimmedStart.StartsWith("• web_run ", StringComparison.Ordinal))
        {
            return "bold fuchsia";
        }

        return "bold cyan";
    }

    private static bool TryGetDiffPreviewLineStyle(
        string trimmedStart,
        out string style)
    {
        style = string.Empty;
        int index = 0;

        while (index < trimmedStart.Length &&
            char.IsDigit(trimmedStart[index]))
        {
            index++;
        }

        if (index == 0 ||
            index + 1 >= trimmedStart.Length ||
            trimmedStart[index] != ' ')
        {
            return false;
        }

        style = trimmedStart[index + 1] switch
        {
            '+' => "green",
            '-' => "red",
            ' ' => "grey",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(style);
    }

    private static bool TryGetMarkdownBullet(
        string trimmedLine,
        out string markerMarkup,
        out string markerPlain,
        out string text)
    {
        markerMarkup = string.Empty;
        markerPlain = string.Empty;
        text = string.Empty;

        if (trimmedLine.Length >= 2 &&
            trimmedLine[0] is '-' or '*' or '+' &&
            char.IsWhiteSpace(trimmedLine[1]))
        {
            markerPlain = "• ";
            markerMarkup = "[green]•[/] ";
            text = trimmedLine[2..].TrimStart();
            return true;
        }

        int digitCount = 0;
        while (digitCount < trimmedLine.Length &&
            char.IsDigit(trimmedLine[digitCount]))
        {
            digitCount++;
        }

        if (digitCount > 0 &&
            digitCount + 1 < trimmedLine.Length &&
            trimmedLine[digitCount] is '.' or ')' &&
            char.IsWhiteSpace(trimmedLine[digitCount + 1]))
        {
            markerPlain = $"{trimmedLine[..digitCount]}{trimmedLine[digitCount]} ";
            markerMarkup = $"[green]{Markup.Escape(markerPlain)}[/]";
            text = trimmedLine[(digitCount + 2)..].TrimStart();
            return true;
        }

        return false;
    }

    private static bool TryReadMarkdownTable(
        string[] rawLines,
        int startIndex,
        out List<string[]> rows,
        out int consumedLineCount)
    {
        rows = [];
        consumedLineCount = 0;

        if (startIndex + 1 >= rawLines.Length ||
            !TryParseMarkdownTableRow(rawLines[startIndex], out string[] headerCells) ||
            !IsMarkdownTableSeparator(rawLines[startIndex + 1]))
        {
            return false;
        }

        rows.Add(headerCells);

        int nextLineIndex = startIndex + 2;
        while (nextLineIndex < rawLines.Length)
        {
            if (string.IsNullOrWhiteSpace(rawLines[nextLineIndex]) ||
                !TryParseMarkdownTableRow(rawLines[nextLineIndex], out string[] rowCells) ||
                IsMarkdownTableSeparator(rawLines[nextLineIndex]))
            {
                break;
            }

            rows.Add(rowCells);
            nextLineIndex++;
        }

        consumedLineCount = nextLineIndex - startIndex;
        return true;
    }

    private static bool TryParseMarkdownTableRow(
        string line,
        out string[] cells)
    {
        cells = [];
        string trimmedLine = line.Trim();

        if (!trimmedLine.Contains('|', StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmedLine.StartsWith('|'))
        {
            trimmedLine = trimmedLine[1..];
        }

        if (trimmedLine.EndsWith('|'))
        {
            trimmedLine = trimmedLine[..^1];
        }

        cells = trimmedLine
            .Split('|', StringSplitOptions.None)
            .Select(cell => cell.Trim())
            .ToArray();

        return cells.Length >= 2;
    }

    private static bool IsMarkdownTableSeparator(string line)
    {
        if (!TryParseMarkdownTableRow(line, out string[] cells))
        {
            return false;
        }

        foreach (string cell in cells)
        {
            string normalizedCell = cell.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (normalizedCell.Length < 3 ||
                !normalizedCell.Contains('-', StringComparison.Ordinal))
            {
                return false;
            }

            foreach (char character in normalizedCell)
            {
                if (character is not ('-' or ':'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int[] CalculateMarkdownTableColumnWidths(
        IReadOnlyList<string[]> rows,
        int columnCount,
        int tableContentWidth,
        bool compact)
    {
        int borderOverhead = compact
            ? columnCount + 1
            : (columnCount * 3) + 1;
        int availableCellWidth = Math.Max(columnCount, tableContentWidth - borderOverhead);
        int[] desiredWidths = new int[columnCount];

        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            desiredWidths[columnIndex] = 1;
        }

        foreach (string[] row in rows)
        {
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                string cellText = columnIndex < row.Length
                    ? row[columnIndex]
                    : string.Empty;
                int desiredWidth = Math.Clamp(GetInlineMarkdownPlainTextLength(cellText), 1, 36);
                desiredWidths[columnIndex] = Math.Max(desiredWidths[columnIndex], desiredWidth);
            }
        }

        int desiredTotalWidth = desiredWidths.Sum();
        if (desiredTotalWidth <= availableCellWidth)
        {
            return desiredWidths;
        }

        int baseColumnWidth = Math.Max(1, availableCellWidth / columnCount);
        int remainingWidth = Math.Max(0, availableCellWidth - (baseColumnWidth * columnCount));
        int[] widths = new int[columnCount];

        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            int fairShare = baseColumnWidth;
            if (remainingWidth > 0)
            {
                fairShare++;
                remainingWidth--;
            }

            widths[columnIndex] = Math.Min(desiredWidths[columnIndex], fairShare);
        }

        int spareWidth = Math.Max(0, availableCellWidth - widths.Sum());
        while (spareWidth > 0)
        {
            bool addedWidth = false;
            for (int columnIndex = 0; columnIndex < columnCount && spareWidth > 0; columnIndex++)
            {
                if (widths[columnIndex] >= desiredWidths[columnIndex])
                {
                    continue;
                }

                widths[columnIndex]++;
                spareWidth--;
                addedWidth = true;
            }

            if (!addedWidth)
            {
                break;
            }
        }

        return widths;
    }

    private static List<InlineRenderResult> RenderMarkdownTableCell(
        string text,
        int columnWidth,
        string lineStyle)
    {
        List<List<MarkdownFragment>> wrappedLines = WrapMarkdownFragments(
            ParseInlineMarkdown(text),
            columnWidth,
            columnWidth);
        List<InlineRenderResult> renderedLines = [];

        foreach (List<MarkdownFragment> wrappedLine in wrappedLines)
        {
            renderedLines.Add(RenderMarkdownFragments(wrappedLine, lineStyle));
        }

        return renderedLines;
    }

    private static void AppendMarkdownTableBorder(
        StringBuilder markup,
        StringBuilder plain)
    {
        markup.Append("[grey]|[/]");
        plain.Append('|');
    }

    private static List<MarkdownFragment> ParseInlineMarkdown(string value)
    {
        List<MarkdownFragment> fragments = [];
        int plainTextStart = 0;
        int index = 0;

        while (index < value.Length)
        {
            if (value[index] == '`')
            {
                int closingIndex = FindClosingMarkdownMarker(value, "`", index + 1);
                if (closingIndex > index)
                {
                    AddMarkdownFragment(fragments, value[plainTextStart..index], string.Empty);
                    AddMarkdownFragment(fragments, value[(index + 1)..closingIndex], "bold yellow");
                    index = closingIndex + 1;
                    plainTextStart = index;
                    continue;
                }
            }

            if (index + 1 < value.Length &&
                value[index] == '*' &&
                value[index + 1] == '*')
            {
                int closingIndex = FindClosingMarkdownMarker(value, "**", index + 2);
                if (closingIndex > index + 1)
                {
                    AddMarkdownFragment(fragments, value[plainTextStart..index], string.Empty);
                    AddMarkdownFragment(fragments, value[(index + 2)..closingIndex], "bold white");
                    index = closingIndex + 2;
                    plainTextStart = index;
                    continue;
                }
            }

            if (index + 1 < value.Length &&
                value[index] == '_' &&
                value[index + 1] == '_')
            {
                int closingIndex = FindClosingMarkdownMarker(value, "__", index + 2);
                if (closingIndex > index + 1)
                {
                    AddMarkdownFragment(fragments, value[plainTextStart..index], string.Empty);
                    AddMarkdownFragment(fragments, value[(index + 2)..closingIndex], "bold white");
                    index = closingIndex + 2;
                    plainTextStart = index;
                    continue;
                }
            }

            if (value[index] is '*' or '_')
            {
                char marker = value[index];
                bool markerIsPartOfPair = index + 1 < value.Length && value[index + 1] == marker;
                if (!markerIsPartOfPair)
                {
                    int closingIndex = value.IndexOf(marker, index + 1);
                    if (closingIndex > index + 1)
                    {
                        AddMarkdownFragment(fragments, value[plainTextStart..index], string.Empty);
                        AddMarkdownFragment(fragments, value[(index + 1)..closingIndex], "italic mediumpurple1");
                        index = closingIndex + 1;
                        plainTextStart = index;
                        continue;
                    }
                }
            }

            index++;
        }

        AddMarkdownFragment(fragments, value[plainTextStart..], string.Empty);
        return fragments;
    }

    private static int FindClosingMarkdownMarker(
        string value,
        string marker,
        int startIndex)
    {
        int closingIndex = value.IndexOf(marker, startIndex, StringComparison.Ordinal);
        return closingIndex > startIndex ? closingIndex : -1;
    }

    private static void AddMarkdownFragment(
        List<MarkdownFragment> fragments,
        string text,
        string style)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (fragments.Count > 0 &&
            fragments[^1].Style == style)
        {
            fragments[^1] = new MarkdownFragment(fragments[^1].Text + text, style);
            return;
        }

        fragments.Add(new MarkdownFragment(text, style));
    }

    private static List<List<MarkdownFragment>> WrapMarkdownFragments(
        IReadOnlyList<MarkdownFragment> fragments,
        int firstLineLength,
        int continuationLineLength)
    {
        int safeFirstLineLength = Math.Max(1, firstLineLength);
        int safeContinuationLineLength = Math.Max(1, continuationLineLength);
        List<List<MarkdownFragment>> wrappedLines = [];
        List<MarkdownFragment> currentLine = [];
        int currentLineLength = 0;
        int currentLimit = safeFirstLineLength;
        bool sawText = false;

        foreach (MarkdownFragment fragment in fragments)
        {
            if (fragment.Text.Length == 0)
            {
                continue;
            }

            sawText = true;
            int fragmentOffset = 0;

            while (fragmentOffset < fragment.Text.Length)
            {
                if (currentLineLength >= currentLimit)
                {
                    wrappedLines.Add(currentLine);
                    currentLine = [];
                    currentLineLength = 0;
                    currentLimit = safeContinuationLineLength;
                }

                int availableLength = currentLimit - currentLineLength;
                int takeLength = Math.Min(availableLength, fragment.Text.Length - fragmentOffset);
                AddMarkdownFragment(
                    currentLine,
                    fragment.Text.Substring(fragmentOffset, takeLength),
                    fragment.Style);
                currentLineLength += takeLength;
                fragmentOffset += takeLength;
            }
        }

        if (!sawText)
        {
            wrappedLines.Add([]);
            return wrappedLines;
        }

        if (currentLine.Count > 0 ||
            wrappedLines.Count == 0)
        {
            wrappedLines.Add(currentLine);
        }

        return wrappedLines;
    }

    private static InlineRenderResult RenderMarkdownFragments(
        IReadOnlyList<MarkdownFragment> fragments,
        string lineStyle)
    {
        StringBuilder markup = new();
        StringBuilder plain = new();
        bool hasLineStyle = !string.IsNullOrWhiteSpace(lineStyle);

        if (hasLineStyle)
        {
            markup.Append('[').Append(lineStyle).Append(']');
        }

        foreach (MarkdownFragment fragment in fragments)
        {
            plain.Append(fragment.Text);

            if (string.IsNullOrWhiteSpace(fragment.Style))
            {
                markup.Append(Markup.Escape(fragment.Text));
                continue;
            }

            markup
                .Append('[')
                .Append(fragment.Style)
                .Append(']')
                .Append(Markup.Escape(fragment.Text))
                .Append("[/]");
        }

        if (hasLineStyle)
        {
            markup.Append("[/]");
        }

        return new InlineRenderResult(markup.ToString(), plain.ToString());
    }

    private static int GetInlineMarkdownPlainTextLength(string text)
    {
        int length = 0;

        foreach (MarkdownFragment fragment in ParseInlineMarkdown(text))
        {
            length += fragment.Text.Length;
        }

        return length;
    }
}
