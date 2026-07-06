using NanoAgent.Application.Models;
using Spectre.Console;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
            string? language = GuessCodeLanguageFromPath(file.DisplayPath);

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
                        contentWidth,
                        language);
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
        int contentWidth,
        string? language = null)
   {
       int availableWidth = GetConversationContentWidth(firstLine, roleName, contentWidth);
       GetFileDiffColumnWidths(availableWidth, out int leftWidth, out int rightWidth);

       string leftPlain = BuildFileDiffCellPlain(row.Left, leftWidth);
       string rightPlain = BuildFileDiffCellPlain(row.Right, rightWidth);
        string leftMarkup = BuildFileDiffCellMarkupWithHighlighting(row.Left, leftPlain, language);
        string rightMarkup = BuildFileDiffCellMarkupWithHighlighting(row.Right, rightPlain, language);


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

   private static string BuildFileDiffCellMarkupWithHighlighting(
       FileEditPreviewEntry? entry,
        string plainText,
        string? language)
    {
        if (entry is null)
        {
            return Markup.Escape(plainText);
        }

        char indicator = entry.Value.Indicator;

        // Context lines: simple grey, no syntax highlighting needed
        if (indicator == ' ')
        {
            return $"[grey]{Markup.Escape(plainText)}[/]";
        }

        // For added/removed lines: apply syntax highlighting and combine with diff indicator style
        string prefix = $"{entry.Value.LineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(FileDiffLineNumberWidth)} {indicator} ";
        string codeText = entry.Value.Text;

        List<MarkdownFragment> fragments = [new MarkdownFragment(prefix, GetDiffIndicatorBaseStyle(indicator))];

        // Apply syntax highlighting to the code text
        foreach (MarkdownFragment fragment in HighlightCodeLines([codeText], language)[0])
        {
            AddMarkdownFragment(
                fragments,
                fragment.Text,
                CombineDiffStyles(fragment.Style, indicator));
        }

        // Calculate and add padding to match the expected cell width
        int padding = Math.Max(0, plainText.Length - (prefix.Length + codeText.Length));
        if (padding > 0)
        {
            AddMarkdownFragment(fragments, new string(' ', padding), GetDiffIndicatorBaseStyle(indicator));
        }

        return RenderMarkdownFragments(fragments, string.Empty).Markup;
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

    private readonly record struct GitPatchFile(
        string DisplayPath,
        string? Language,
        IReadOnlyList<GitPatchHunk> Hunks);

    private readonly record struct GitPatchHunk(
        string Header,
        IReadOnlyList<FileEditPreviewEntry> Entries);

    private static bool TryBuildGitPatchReaderLines(
        string patch,
        int width,
        out IReadOnlyList<ReaderViewLine> lines)
    {
        lines = [];

        string[] rawLines = patch
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        List<GitPatchFile> files = ParseGitPatchFiles(rawLines);
        if (files.Count == 0)
        {
            return false;
        }

        int contentWidth = Math.Max(12, width);
        List<ReaderViewLine> rendered = [];

        foreach (GitPatchFile file in files)
        {
            if (rendered.Count > 0)
            {
                rendered.Add(new ReaderViewLine(string.Empty, string.Empty));
            }

            string headerPlain = file.DisplayPath;
            string headerMarkup = $"[bold]{Markup.Escape(headerPlain)}[/]";
            rendered.Add(new ReaderViewLine(headerMarkup, headerPlain));

            rendered.Add(BuildReaderDiffColumnHeader(contentWidth));

            foreach (GitPatchHunk hunk in file.Hunks)
            {
                string hunkPlain = hunk.Header;
                string hunkMarkup = $"[grey]{Markup.Escape(hunkPlain)}[/]";
                rendered.Add(new ReaderViewLine(hunkMarkup, hunkPlain));

                foreach (FileEditRow row in BuildFileEditRows(hunk.Entries))
                {
                    rendered.Add(BuildReaderDiffRow(row, contentWidth, file.Language));
                }
            }
        }

        lines = rendered;
        return rendered.Count > 0;
    }

    private static List<GitPatchFile> ParseGitPatchFiles(IReadOnlyList<string> rawLines)
    {
        List<GitPatchFile> files = [];
        string? pendingOldPath = null;
        string? pendingNewPath = null;
        string? currentPath = null;
        string? currentLanguage = null;
        List<GitPatchHunk> currentHunks = [];
        string? currentHunkHeader = null;
        List<FileEditPreviewEntry>? currentEntries = null;
        int oldLineNumber = 0;
        int newLineNumber = 0;

        void FinalizeHunk()
        {
            if (currentHunkHeader is null || currentEntries is null)
            {
                return;
            }

            currentHunks.Add(new GitPatchHunk(currentHunkHeader, [.. currentEntries]));
            currentHunkHeader = null;
            currentEntries = null;
        }

        void FinalizeFile()
        {
            FinalizeHunk();
            if (string.IsNullOrWhiteSpace(currentPath) || currentHunks.Count == 0)
            {
                currentPath = null;
                currentLanguage = null;
                currentHunks = [];
                return;
            }

            files.Add(new GitPatchFile(currentPath, currentLanguage, [.. currentHunks]));
            currentPath = null;
            currentLanguage = null;
            currentHunks = [];
        }

        foreach (string rawLine in rawLines)
        {
            if (rawLine.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FinalizeFile();
                pendingOldPath = null;
                pendingNewPath = null;
                continue;
            }

            if (rawLine.StartsWith("--- ", StringComparison.Ordinal))
            {
                pendingOldPath = NormalizeGitPatchPath(rawLine[4..]);
                continue;
            }

            if (rawLine.StartsWith("+++ ", StringComparison.Ordinal))
            {
                pendingNewPath = NormalizeGitPatchPath(rawLine[4..]);
                currentPath = pendingNewPath ?? pendingOldPath;
                currentLanguage = GuessCodeLanguageFromPath(currentPath);
                continue;
            }

            if (rawLine.StartsWith("@@ ", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(currentPath))
                {
                    continue;
                }

                FinalizeHunk();
                currentHunkHeader = rawLine;
                currentEntries = [];
                ParseGitPatchHunkHeader(rawLine, out oldLineNumber, out newLineNumber);
                continue;
            }

            if (currentEntries is null || rawLine.Length == 0)
            {
                continue;
            }

            char indicator = rawLine[0];
            string text = rawLine.Length > 1 ? rawLine[1..] : string.Empty;
            switch (indicator)
            {
                case ' ':
                    currentEntries.Add(new FileEditPreviewEntry(oldLineNumber, ' ', text));
                    oldLineNumber++;
                    newLineNumber++;
                    break;
                case '-':
                    currentEntries.Add(new FileEditPreviewEntry(oldLineNumber, '-', text));
                    oldLineNumber++;
                    break;
                case '+':
                    currentEntries.Add(new FileEditPreviewEntry(newLineNumber, '+', text));
                    newLineNumber++;
                    break;
            }
        }

        FinalizeFile();
        return files;
    }

    private static ReaderViewLine BuildReaderDiffColumnHeader(int width)
    {
        GetFileDiffColumnWidths(width, out int leftWidth, out int rightWidth);
        string leftText = FitDiffCell("before", leftWidth, pad: true);
        string rightText = FitDiffCell("after", rightWidth, pad: true);
        string plain = leftText + FileDiffSeparatorPlain + rightText;
        string markup = $"[grey]{Markup.Escape(leftText)}[/][grey]{Markup.Escape(FileDiffSeparatorPlain)}[/][grey]{Markup.Escape(rightText)}[/]";
        return new ReaderViewLine(markup, plain);
    }

    private static ReaderViewLine BuildReaderDiffRow(
        FileEditRow row,
        int width,
        string? language)
    {
        GetFileDiffColumnWidths(width, out int leftWidth, out int rightWidth);
        InlineRenderResult left = BuildReaderDiffCell(row.Left, leftWidth, language);
        InlineRenderResult right = BuildReaderDiffCell(row.Right, rightWidth, language);
        return new ReaderViewLine(
            left.Markup + "[grey] | [/]" + right.Markup,
            left.Plain + FileDiffSeparatorPlain + right.Plain);
    }

    private static InlineRenderResult BuildReaderDiffCell(
        FileEditPreviewEntry? entry,
        int width,
        string? language)
    {
        if (entry is null)
        {
            string blank = new string(' ', Math.Max(1, width));
            return new InlineRenderResult(Markup.Escape(blank), blank);
        }

        string prefix = $"{entry.Value.LineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(FileDiffLineNumberWidth)} {(entry.Value.Indicator == ' ' ? " " : entry.Value.Indicator.ToString(CultureInfo.InvariantCulture))} ";
        string codeText = entry.Value.Text;
        string combinedPlain = FitDiffCell(prefix + codeText, width, pad: true);

        if (entry.Value.Indicator == ' ')
        {
            return new InlineRenderResult($"[grey]{Markup.Escape(combinedPlain)}[/]", combinedPlain);
        }

        List<MarkdownFragment> fragments = [new MarkdownFragment(prefix, GetDiffIndicatorBaseStyle(entry.Value.Indicator))];
        foreach (MarkdownFragment fragment in HighlightCodeLines([codeText], language)[0])
        {
            AddMarkdownFragment(
                fragments,
                fragment.Text,
                CombineDiffStyles(fragment.Style, entry.Value.Indicator));
        }

        int padding = Math.Max(0, width - (prefix.Length + codeText.Length));
        if (padding > 0)
        {
            AddMarkdownFragment(fragments, new string(' ', padding), GetDiffIndicatorBaseStyle(entry.Value.Indicator));
        }

        return RenderMarkdownFragments(fragments, string.Empty);
    }

    private static string GetDiffIndicatorBaseStyle(char indicator)
    {
        return indicator switch
        {
            '-' => "white on red",
            '+' => "black on green",
            _ => "grey"
        };
    }

    private static string CombineDiffStyles(string syntaxStyle, char indicator)
    {
        string baseStyle = GetDiffIndicatorBaseStyle(indicator);
        if (string.IsNullOrWhiteSpace(syntaxStyle))
        {
            return baseStyle;
        }

        int backgroundIndex = baseStyle.IndexOf(" on ", StringComparison.Ordinal);
        if (backgroundIndex < 0)
        {
            return syntaxStyle;
        }

        return syntaxStyle + baseStyle[backgroundIndex..];
    }

    private static void ParseGitPatchHunkHeader(string rawLine, out int oldLineNumber, out int newLineNumber)
    {
        Match match = Regex.Match(rawLine, @"^@@ -(?<old>\d+)(?:,\d+)? \+(?<new>\d+)(?:,\d+)? @@");
        oldLineNumber = match.Success && int.TryParse(match.Groups["old"].Value, out int parsedOld)
            ? parsedOld
            : 1;
        newLineNumber = match.Success && int.TryParse(match.Groups["new"].Value, out int parsedNew)
            ? parsedNew
            : 1;
    }

    private static string? NormalizeGitPatchPath(string rawPath)
    {
        string trimmed = rawPath.Trim();
        if (string.Equals(trimmed, "/dev/null", StringComparison.Ordinal))
        {
            return null;
        }

        if (trimmed.StartsWith("a/", StringComparison.Ordinal) ||
            trimmed.StartsWith("b/", StringComparison.Ordinal))
        {
            return trimmed[2..];
        }

        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? GuessCodeLanguageFromPath(string? path)
    {
        string extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "cs",
            ".fs" => "fs",
            ".vb" => "vb",
            ".js" => "js",
            ".jsx" => "jsx",
            ".ts" => "ts",
            ".tsx" => "tsx",
            ".json" or ".jsonl" => "json",
            ".xml" or ".csproj" or ".props" or ".targets" or ".svg" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".toml" or ".ini" => "toml",
            ".md" => "md",
            ".html" or ".htm" => "html",
            ".css" or ".scss" => "css",
            ".py" => "py",
            ".go" => "go",
            ".rs" => "rust",
            ".java" => "java",
            ".c" or ".h" or ".cpp" or ".hpp" => "cpp",
            ".sql" => "sql",
            ".sh" => "sh",
            ".ps1" => "powershell",
            _ => null
        };
    }
}
