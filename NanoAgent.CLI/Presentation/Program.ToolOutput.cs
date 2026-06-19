using Spectre.Console;

namespace NanoAgent.CLI;

public static partial class Program
{
    private const string ToolPreviewReadPrefix = "• Read ";
    private const string ToolPreviewSavedReadMarker = "file read: ";
    private const string ToolPreviewSavedWriteMarker = "file write: ";

    private enum ToolPreviewMode
    {
        None,
        FileContent,
        FileEdit,
    }

    private struct ToolOutputHighlightContext
    {
        public string? Language;
        public ToolPreviewMode Mode;

        // Carries multi-line highlight state (block comments, embedded <style>/<script>
        // regions) across the numbered code lines of a single file-read preview, so large
        // files highlight as consistently as a fenced code block. Reset by every new
        // file/edit header below.
        public CodeHighlightState HighlightState;
    }

    // Tracks the file/language context of tool-output preview lines so that the code
    // shown for file reads and edits can be syntax highlighted. Header lines (for
    // example "• Read src/app.cs (...)" or "  - src/app.cs (+3 -1)") set the active
    // language and preview mode; the actual numbered code lines reuse it.
    private static void UpdateToolOutputHighlightContext(
        string rawLine,
        ref ToolOutputHighlightContext context)
    {
        if (rawLine.StartsWith(ToolPreviewReadPrefix, StringComparison.Ordinal))
        {
            string remainder = rawLine[ToolPreviewReadPrefix.Length..];
            int suffixIndex = remainder.LastIndexOf(" (", StringComparison.Ordinal);
            string path = suffixIndex >= 0 ? remainder[..suffixIndex] : remainder;
            context.Language = ExtractLanguageFromPath(path);
            context.Mode = ToolPreviewMode.FileContent;
            context.HighlightState = default;
            return;
        }

        if (TryGetSavedPreviewPath(rawLine, out string savedPath))
        {
            context.Language = ExtractLanguageFromPath(savedPath);
            context.Mode = ToolPreviewMode.FileContent;
            context.HighlightState = default;
            return;
        }

        if (TryGetEditHeaderPath(rawLine, out string editPath))
        {
            context.Language = ExtractLanguageFromPath(editPath);
            context.Mode = ToolPreviewMode.FileEdit;
            context.HighlightState = default;
        }
    }

    private static bool TryGetSavedPreviewPath(string rawLine, out string path)
    {
        path = string.Empty;

        if (!rawLine.StartsWith("• Previewed saved tool call:", StringComparison.Ordinal))
        {
            return false;
        }

        int markerIndex = rawLine.IndexOf(ToolPreviewSavedWriteMarker, StringComparison.Ordinal);
        int markerLength = ToolPreviewSavedWriteMarker.Length;
        if (markerIndex < 0)
        {
            markerIndex = rawLine.IndexOf(ToolPreviewSavedReadMarker, StringComparison.Ordinal);
            markerLength = ToolPreviewSavedReadMarker.Length;
        }

        if (markerIndex < 0)
        {
            return false;
        }

        path = rawLine[(markerIndex + markerLength)..].Trim();
        return path.Length > 0;
    }

    private static bool TryGetEditHeaderPath(string rawLine, out string path)
    {
        path = string.Empty;

        // A per-file edit header looks like "  - <displayPath> (+<added> -<removed>)".
        if (!rawLine.StartsWith("  - ", StringComparison.Ordinal) ||
            !rawLine.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        int suffixIndex = rawLine.LastIndexOf(" (+", StringComparison.Ordinal);
        if (suffixIndex < 4)
        {
            return false;
        }

        string displayPath = rawLine[4..suffixIndex];

        // Renamed files render as "old -> new"; highlight using the new path.
        int renameIndex = displayPath.LastIndexOf(" -> ", StringComparison.Ordinal);
        if (renameIndex >= 0)
        {
            displayPath = displayPath[(renameIndex + 4)..];
        }

        path = displayPath.Trim();
        return path.Length > 0;
    }

    private static string? ExtractLanguageFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim();
        int lastSeparator = trimmed.LastIndexOfAny(['/', '\\']);
        string fileName = lastSeparator >= 0 ? trimmed[(lastSeparator + 1)..] : trimmed;
        int dotIndex = fileName.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex == fileName.Length - 1)
        {
            return null;
        }

        return fileName[(dotIndex + 1)..].ToLowerInvariant();
    }

    // Renders a tool-output preview code line with syntax highlighting when it matches
    // the numbered preview layout produced by ToolOutputFormatter. Returns false for
    // every other line so the caller falls back to the default tool-output styling.
    private static bool TryAddHighlightedToolOutputLine(
        List<ConversationLine> lines,
        string rawLine,
        ref ToolOutputHighlightContext context,
        ref bool firstLine,
        string roleName,
        string roleColor,
        int contentWidth)
    {
        if (context.Mode == ToolPreviewMode.None)
        {
            return false;
        }

        bool expectIndicator = context.Mode == ToolPreviewMode.FileEdit;
        if (!TryParseToolPreviewCodeLine(
                rawLine,
                expectIndicator,
                out string prefix,
                out string code,
                out char indicator))
        {
            return false;
        }

        string prefixStyle = indicator switch
        {
            '+' => "green",
            '-' => "red",
            _ => "grey",
        };
        string prefixMarkup = $"[{prefixStyle}]{Markup.Escape(prefix)}[/]";
        string continuationPlain = new(' ', prefix.Length);

        List<MarkdownFragment> fragments = [];
        CodeLanguageSyntax syntax = GetCodeLanguageSyntax(context.Language);

        if (context.Mode == ToolPreviewMode.FileContent)
        {
            // File reads show contiguous lines, so carry multi-line state forward to keep
            // block comments and embedded <style>/<script> regions highlighting correctly.
            HighlightLine(code, syntax, fragments, ref context.HighlightState);
        }
        else
        {
            // Edit previews interleave added/removed/context lines from different file
            // versions, so highlight each line independently to avoid one hunk's open
            // construct bleeding into another.
            CodeHighlightState lineState = default;
            HighlightLine(code, syntax, fragments, ref lineState);
        }

        AddWrappedFragmentLine(
            lines,
            fragments,
            prefixMarkup,
            prefix,
            continuationPlain,
            continuationPlain,
            ref firstLine,
            roleName,
            roleColor,
            contentWidth);

        return true;
    }

    // Matches "      <num> <code>" (file content) or "      <num> <±><code>" (edits),
    // where the leading indent and right-aligned line number are produced by
    // ToolOutputFormatter. The returned prefix covers everything up to the code text.
    private static bool TryParseToolPreviewCodeLine(
        string rawLine,
        bool expectIndicator,
        out string prefix,
        out string code,
        out char indicator)
    {
        prefix = string.Empty;
        code = string.Empty;
        indicator = ' ';

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

        int digitStart = index;
        while (index < rawLine.Length && char.IsDigit(rawLine[index]))
        {
            index++;
        }

        if (index == digitStart ||
            index >= rawLine.Length ||
            rawLine[index] != ' ')
        {
            return false;
        }

        index++;

        if (expectIndicator)
        {
            if (index >= rawLine.Length || rawLine[index] is not ('+' or '-' or ' '))
            {
                return false;
            }

            indicator = rawLine[index];
            index++;
        }

        prefix = rawLine[..index];
        code = rawLine[index..];
        return true;
    }
}
