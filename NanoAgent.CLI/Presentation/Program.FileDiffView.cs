using NanoAgent.Application.Models;
using Spectre.Console;
using System.Globalization;
using System.Text;

namespace NanoAgent.CLI;

public static partial class Program
{
    private const string FileEditSummaryPrefix = "\u2022 Edited ";
    private const string FileEditHeaderPrefix = "  - ";
    private const string FileEditCountSuffixPrefix = " (+";
    private const string FileEditOmittedPrefix = "    ... +";
    private const string FileDiffSeparatorPlain = " | ";
    private const int FileDiffLineNumberWidth = 4;

    private static bool TryReadFileEditDiffBlock(
        string[] rawLines,
        int startIndex,
        out FileEditDiffBlock block,
        out int consumedLineCount)
    {
        block = default;
        consumedLineCount = 0;

        if (startIndex < 0 ||
            startIndex >= rawLines.Length ||
            !rawLines[startIndex].StartsWith(FileEditSummaryPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        List<FileEditDiffFile> files = [];
        int index = startIndex + 1;

        while (index < rawLines.Length)
        {
            string candidate = rawLines[index];
            if (!TryParseFileEditHeader(candidate, out string displayPath))
            {
                break;
            }

            string headerLine = candidate;
            index++;

            List<FileEditPreviewEntry> previewEntries = [];
            string? omittedLine = null;

            while (index < rawLines.Length)
            {
                string line = rawLines[index];

                if (TryParseFileEditHeader(line, out _))
                {
                    break;
                }

                if (TryParseFileEditPreviewEntry(line, out FileEditPreviewEntry previewEntry))
                {
                    previewEntries.Add(previewEntry);
                    index++;
                    continue;
                }

                if (line.StartsWith(FileEditOmittedPrefix, StringComparison.Ordinal))
                {
                    omittedLine = line;
                    index++;
                    continue;
                }

                break;
            }

            files.Add(new FileEditDiffFile(headerLine, displayPath, previewEntries, omittedLine));
        }

        if (files.Count == 0)
        {
            return false;
        }

        consumedLineCount = Math.Max(1, index - startIndex);
        block = new FileEditDiffBlock(rawLines[startIndex], files);
        return true;
    }

    private static void AddFileEditDiffBlockLines(
        List<ConversationLine> lines,
        FileEditDiffBlock block,
        ref bool firstLine,
        string roleName,
        string roleColor,
        int contentWidth)
    {
        AddMarkdownTextLine(
            lines,
            block.SummaryLine,
            ref firstLine,
            roleName,
            roleColor,
            Role.System,
            contentWidth);

        foreach (FileEditDiffFile file in block.Files)
        {
            AddMarkdownTextLine(
                lines,
                file.HeaderLine,
                ref firstLine,
                roleName,
                roleColor,
                Role.System,
                contentWidth);

            if (file.PreviewEntries.Count > 0)
            {
                AddFileDiffColumnHeaderLine(
                    lines,
                    ref firstLine,
                    roleName,
                    roleColor,
                    contentWidth);

                foreach (FileEditRow row in BuildFileEditRows(file.PreviewEntries))
                {
                    AddFileDiffRowLine(
                        lines,
                        row,
                        ref firstLine,
                        roleName,
                        roleColor,
                        contentWidth);
                }
            }

            if (!string.IsNullOrWhiteSpace(file.OmittedLine))
            {
                AddMarkdownTextLine(
                    lines,
                    file.OmittedLine!,
                    ref firstLine,
                    roleName,
                    roleColor,
                    Role.System,
                    contentWidth);
            }
        }
    }

    private static void AddFileDiffColumnHeaderLine(
        List<ConversationLine> lines,
        ref bool firstLine,
        string roleName,
        string roleColor,
        int contentWidth)
    {
        int availableWidth = GetConversationContentWidth(firstLine, roleName, contentWidth);
        GetFileDiffColumnWidths(availableWidth, out int leftWidth, out int rightWidth);

        string leftText = FitDiffCell("before", leftWidth, pad: true);
        string rightText = FitDiffCell("after", rightWidth, pad: true);
        string markup = $"[grey]{Markup.Escape(leftText)}[/][grey]{Markup.Escape(FileDiffSeparatorPlain)}[/][grey]{Markup.Escape(rightText)}[/]";
        string plain = leftText + FileDiffSeparatorPlain + rightText;

        AddConversationContentLine(
            lines,
            markup,
            plain,
            ref firstLine,
            roleName,
            roleColor);
    }

    private static void AddFileDiffRowLine(
        List<ConversationLine> lines,
        FileEditRow row,
        ref bool firstLine,
        string roleName,
        string roleColor,
        int contentWidth)
    {
        int availableWidth = GetConversationContentWidth(firstLine, roleName, contentWidth);
        GetFileDiffColumnWidths(availableWidth, out int leftWidth, out int rightWidth);

        string leftPlain = BuildFileDiffCellPlain(row.Left, leftWidth);
        string rightPlain = BuildFileDiffCellPlain(row.Right, rightWidth);
        string leftMarkup = BuildFileDiffCellMarkup(row.Left, leftPlain);
        string rightMarkup = BuildFileDiffCellMarkup(row.Right, rightPlain);

        AddConversationContentLine(
            lines,
            leftMarkup + "[grey] | [/]" + rightMarkup,
            leftPlain + FileDiffSeparatorPlain + rightPlain,
            ref firstLine,
            roleName,
            roleColor);
    }

    private static IReadOnlyList<FileEditRow> BuildFileEditRows(
        IReadOnlyList<FileEditPreviewEntry> previewEntries)
    {
        List<FileEditRow> rows = [];

        for (int index = 0; index < previewEntries.Count;)
        {
            FileEditPreviewEntry entry = previewEntries[index];

            if (entry.Indicator == ' ')
            {
                rows.Add(new FileEditRow(entry, entry));
                index++;
                continue;
            }

            if (entry.Indicator is not ('+' or '-'))
            {
                index++;
                continue;
            }

            char firstIndicator = entry.Indicator;
            List<FileEditPreviewEntry> firstGroup = [];
            while (index < previewEntries.Count &&
                previewEntries[index].Indicator == firstIndicator)
            {
                firstGroup.Add(previewEntries[index]);
                index++;
            }

            char secondIndicator = firstIndicator == '-'
                ? '+'
                : '-';
            List<FileEditPreviewEntry> secondGroup = [];
            while (index < previewEntries.Count &&
                previewEntries[index].Indicator == secondIndicator)
            {
                secondGroup.Add(previewEntries[index]);
                index++;
            }

            bool firstGroupIsLeft = firstIndicator == '-';
            int rowCount = Math.Max(firstGroup.Count, secondGroup.Count);
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                FileEditPreviewEntry? left = firstGroupIsLeft
                    ? GetPreviewEntryOrNull(firstGroup, rowIndex)
                    : GetPreviewEntryOrNull(secondGroup, rowIndex);
                FileEditPreviewEntry? right = firstGroupIsLeft
                    ? GetPreviewEntryOrNull(secondGroup, rowIndex)
                    : GetPreviewEntryOrNull(firstGroup, rowIndex);

                rows.Add(new FileEditRow(left, right));
            }
        }

        return rows;
    }

    private static FileEditPreviewEntry? GetPreviewEntryOrNull(
        IReadOnlyList<FileEditPreviewEntry> entries,
        int index)
    {
        return index >= 0 && index < entries.Count
            ? entries[index]
            : null;
    }

    private static string BuildFileDiffCellPlain(
        FileEditPreviewEntry? entry,
        int width)
    {
        if (entry is null)
        {
            return new string(' ', Math.Max(1, width));
        }

        string indicator = entry.Value.Indicator == ' '
            ? " "
            : entry.Value.Indicator.ToString(CultureInfo.InvariantCulture);
        string text = $"{entry.Value.LineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(FileDiffLineNumberWidth)} {indicator} {entry.Value.Text}";
        return FitDiffCell(text, width, pad: true);
    }

    private static string BuildFileDiffCellMarkup(
        FileEditPreviewEntry? entry,
        string plainText)
    {
        if (entry is null)
        {
            return Markup.Escape(plainText);
        }

        string style = entry.Value.Indicator switch
        {
            '-' => "white on red",
            '+' => "black on green",
            _ => "grey"
        };

        return $"[{style}]{Markup.Escape(plainText)}[/]";
    }

    private static int GetConversationContentWidth(
        bool firstLine,
        string roleName,
        int contentWidth)
    {
        int prefixLength = firstLine
            ? roleName.Length + 2
            : 5;

        return Math.Max(1, contentWidth - prefixLength);
    }

    private static void GetFileDiffColumnWidths(
        int availableWidth,
        out int leftWidth,
        out int rightWidth)
    {
        int safeWidth = Math.Max(3, availableWidth);
        int remainingWidth = Math.Max(0, safeWidth - FileDiffSeparatorPlain.Length);
        leftWidth = Math.Max(1, remainingWidth / 2);
        rightWidth = Math.Max(1, remainingWidth - leftWidth);
    }

    private static string FitDiffCell(
        string text,
        int width,
        bool pad)
    {
        int safeWidth = Math.Max(1, width);
        string normalized = text ?? string.Empty;

        if (normalized.Length > safeWidth)
        {
            normalized = safeWidth <= 3
                ? normalized[..safeWidth]
                : normalized[..(safeWidth - 3)] + "...";
        }

        return pad
            ? normalized.PadRight(safeWidth)
            : normalized;
    }

    // Renders the running "files modified" tally as a pipe-bordered table: Action, +added (green),
    // -removed (red), and the file name as a clickable link (opens in VS Code when it's on PATH,
    // otherwise the OS default editor for the extension). A title line and totals row bookend it.
    private static void AddFileEditsSummaryLines(
        List<ConversationLine> lines,
        IReadOnlyList<FileEditSummary> files,
        ref bool firstLine,
        string roleName,
        string roleColor,
        int contentWidth)
    {
        int totalAdded = files.Sum(static f => f.AddedLineCount);
        int totalRemoved = files.Sum(static f => f.RemovedLineCount);
        bool vscode = IsVsCodeAvailable();

        // Each cell carries plain text (for width/alignment) and markup (for color/links).
        string[] headers = ["Action", "Added", "Removed", "File"];
        List<(string Markup, string Plain)[]> rows =
        [
            [.. headers.Select(h => ($"[bold]{h}[/]", h))],
        ];

        foreach (FileEditSummary file in files)
        {
            string added = "+" + file.AddedLineCount;
            string removed = "-" + file.RemovedLineCount;
            string actionColor = file.Action switch
            {
                "Created" => "green",
                "Deleted" => "red",
                _ => "yellow",
            };
            string? link = BuildEditorLink(file.AbsolutePath, vscode);
            string escapedPath = Markup.Escape(file.DisplayPath);

            rows.Add(
            [
                ($"[{actionColor}]{file.Action}[/]", file.Action),
                ($"[green]{added}[/]", added),
                ($"[red]{removed}[/]", removed),
                (link is null ? escapedPath : $"[link={link}]{escapedPath}[/]", file.DisplayPath),
            ]);
        }

        string totalAddedText = "+" + totalAdded;
        string totalRemovedText = "-" + totalRemoved;
        rows.Add(
        [
            ("[grey]Total[/]", "Total"),
            ($"[green]{totalAddedText}[/]", totalAddedText),
            ($"[red]{totalRemovedText}[/]", totalRemovedText),
            ("[grey]" + files.Count + " file(s)[/]", files.Count + " file(s)"),
        ]);

        int columns = headers.Length;
        int[] widths = new int[columns];
        foreach ((string, string Plain)[] row in rows)
        {
            for (int c = 0; c < columns; c++)
            {
                widths[c] = Math.Max(widths[c], row[c].Plain.Length);
            }
        }

        // Right-align the two numeric columns; left-align Action and File.
        bool[] rightAlign = [false, true, true, false];

        AddConversationContentLine(
            lines,
            $"[bold]Files modified ({files.Count})[/]",
            $"Files modified ({files.Count})",
            ref firstLine,
            roleName,
            roleColor);

        for (int r = 0; r < rows.Count; r++)
        {
            AddSummaryTableRow(lines, rows[r], widths, rightAlign, ref firstLine, roleName, roleColor);
            if (r == 0)
            {
                AddSummaryTableSeparator(lines, widths, ref firstLine, roleName, roleColor);
            }
        }
    }

    private static void AddSummaryTableRow(
        List<ConversationLine> lines,
        (string Markup, string Plain)[] cells,
        int[] widths,
        bool[] rightAlign,
        ref bool firstLine,
        string roleName,
        string roleColor)
    {
        StringBuilder markup = new("[grey]|[/]");
        StringBuilder plain = new("|");

        for (int c = 0; c < widths.Length; c++)
        {
            string pad = new(' ', Math.Max(0, widths[c] - cells[c].Plain.Length));
            string cellMarkup = rightAlign[c] ? pad + cells[c].Markup : cells[c].Markup + pad;
            string cellPlain = rightAlign[c] ? pad + cells[c].Plain : cells[c].Plain + pad;

            markup.Append(' ').Append(cellMarkup).Append(' ').Append("[grey]|[/]");
            plain.Append(' ').Append(cellPlain).Append(' ').Append('|');
        }

        AddConversationContentLine(lines, markup.ToString(), plain.ToString(), ref firstLine, roleName, roleColor);
    }

    private static void AddSummaryTableSeparator(
        List<ConversationLine> lines,
        int[] widths,
        ref bool firstLine,
        string roleName,
        string roleColor)
    {
        StringBuilder markup = new("[grey]|[/]");
        StringBuilder plain = new("|");

        foreach (int width in widths)
        {
            string dashes = new('-', width);
            markup.Append("[grey] ").Append(dashes).Append(" |[/]");
            plain.Append(' ').Append(dashes).Append(" |");
        }

        AddConversationContentLine(lines, markup.ToString(), plain.ToString(), ref firstLine, roleName, roleColor);
    }

    // Returns null when the path can't form a valid file URI (e.g. relative/odd paths), so
    // the caller renders the file name without a clickable link instead of crashing.
    private static string? BuildEditorLink(string absolutePath, bool vscode)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return null;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(absolutePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        Uri uri = new(normalizedPath);
        if (!uri.IsFile)
        {
            return null;
        }

        string fileUri = uri.AbsoluteUri; // file:///F:/path/file.cs
        return vscode
            ? "vscode://file/" + fileUri["file:///".Length..]
            : fileUri;
    }

    private static bool IsVsCodeAvailable()
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator)
            .Where(static dir => !string.IsNullOrWhiteSpace(dir))
            .Any(static dir =>
                File.Exists(Path.Combine(dir, "code.cmd")) ||
                File.Exists(Path.Combine(dir, "code.exe")) ||
                File.Exists(Path.Combine(dir, "code")));
    }

    private static bool TryParseFileEditHeader(
        string rawLine,
        out string displayPath)
    {
        displayPath = string.Empty;

        if (!rawLine.StartsWith(FileEditHeaderPrefix, StringComparison.Ordinal) ||
            !rawLine.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        int suffixIndex = rawLine.LastIndexOf(FileEditCountSuffixPrefix, StringComparison.Ordinal);
        if (suffixIndex < FileEditHeaderPrefix.Length)
        {
            return false;
        }

        displayPath = rawLine[FileEditHeaderPrefix.Length..suffixIndex].Trim();
        return displayPath.Length > 0;
    }

    private static bool TryParseFileEditPreviewEntry(
        string rawLine,
        out FileEditPreviewEntry entry)
    {
        entry = default;

        const int previewIndent = 6;
        if (rawLine.Length <= previewIndent ||
            !rawLine.AsSpan(0, previewIndent).IsWhiteSpace())
        {
            return false;
        }

        int index = previewIndent;
        while (index < rawLine.Length && rawLine[index] == ' ')
        {
            index++;
        }

        int digitsStart = index;
        while (index < rawLine.Length && char.IsDigit(rawLine[index]))
        {
            index++;
        }

        if (index == digitsStart ||
            index >= rawLine.Length ||
            rawLine[index] != ' ' ||
            !int.TryParse(
                rawLine.AsSpan(digitsStart, index - digitsStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int lineNumber))
        {
            return false;
        }

        index++;
        if (index >= rawLine.Length ||
            rawLine[index] is not ('+' or '-' or ' '))
        {
            return false;
        }

        char indicator = rawLine[index];
        string text = index + 1 < rawLine.Length
            ? rawLine[(index + 1)..]
            : string.Empty;

        entry = new FileEditPreviewEntry(lineNumber, indicator, text);
        return true;
    }

    private readonly record struct FileEditDiffBlock(
        string SummaryLine,
        IReadOnlyList<FileEditDiffFile> Files);

    private readonly record struct FileEditDiffFile(
        string HeaderLine,
        string DisplayPath,
        IReadOnlyList<FileEditPreviewEntry> PreviewEntries,
        string? OmittedLine);

    private readonly record struct FileEditPreviewEntry(
        int LineNumber,
        char Indicator,
        string Text);

    private readonly record struct FileEditRow(
        FileEditPreviewEntry? Left,
        FileEditPreviewEntry? Right);
}
