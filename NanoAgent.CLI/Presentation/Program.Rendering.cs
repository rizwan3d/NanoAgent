using Spectre.Console;
using Spectre.Console.Rendering;

namespace NanoAgent.CLI;

public static partial class Program
{
    private static IRenderable BuildUi(AppState state)
    {
        Layout root = new Layout("root")
            .SplitRows(
                new Layout("header").Size(HeaderPanelSize),
                new Layout("body").Ratio(1),
                new Layout("input").Size(GetInputPanelSize(state)),
                new Layout("footer").Size(1));

        root["header"].Update(BuildHeader(state));

        if (state.ActiveModal is not null)
        {
            Layout bodySplit = new Layout("body-split")
                .SplitRows(
                    new Layout("messages").Ratio(1),
                    new Layout("prompt").Size(state.ActiveModal.PanelSize));

            bodySplit["messages"].Update(BuildMessagesPanel(state));
            bodySplit["prompt"].Update(BuildPromptPanel(state.ActiveModal));
            root["body"].Update(bodySplit);
        }
        else
        {
            root["body"].Update(BuildMessagesPanel(state));
        }

        root["input"].Update(BuildInputPanel(state));
        root["footer"].Update(new Markup(BuildFooterMarkup(state)));

        return root;
    }

    private static IRenderable BuildHeader(AppState state)
    {
        string spinner = state.IsBusy || state.IsStreaming
            ? Spinner[state.SpinnerFrame / 4 % Spinner.Length]
            : " ";

        string model = Markup.Escape(state.ActiveModelId ?? "n/a");
        string statusHeader =
            $"[bold cyan]NanoAgent[/] " +
            $"[grey]model:[/] [aqua]{model}[/] " +
            $"[grey]GitHub:[/] [deepskyblue1]{Markup.Escape(RepositoryUrl)}[/] " +
            $"[yellow]{spinner}[/]";

        return new Panel(new Markup(BuildHeaderMarkup(state)))
            .Header(statusHeader)
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private static IRenderable BuildMessagesPanel(AppState state)
    {
        return new Panel(new Markup(BuildMessagesMarkup(state)))
            .Header("[bold]Conversation[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private static IRenderable BuildPromptPanel(UiModalState modal)
    {
        return new Panel(new Markup(modal.BuildBodyMarkup()))
            .Header("[bold yellow]Action Required[/]")
            .Border(BoxBorder.Double)
            .Expand();
    }

    private static IRenderable BuildInputPanel(AppState state)
    {
        return new Panel(new Markup(BuildInputMarkup(state)))
            .Header("[bold green]Input[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private static string BuildMessagesMarkup(AppState state)
    {
        int viewportLineCount = GetMessageViewportLineCount(state);
        int contentWidth = GetMessageContentWidth();
        List<ConversationLine> lines = BuildConversationLines(state, contentWidth);

        if (lines.Count == 0)
        {
            lines.Add(new ConversationLine("[grey]No messages yet.[/]", "No messages yet."));
        }

        int maxScrollOffset = Math.Max(0, lines.Count - viewportLineCount);
        state.ConversationScrollOffset = Math.Clamp(
            state.ConversationScrollOffset,
            0,
            maxScrollOffset);
        int startIndex = Math.Max(
            0,
            lines.Count - viewportLineCount - state.ConversationScrollOffset);

        List<ConversationLine> visibleLines = lines
            .Skip(startIndex)
            .Take(viewportLineCount)
            .ToList();

        while (visibleLines.Count < viewportLineCount)
        {
            visibleLines.Add(new ConversationLine(string.Empty, string.Empty));
        }

        return BuildScrollableConversationMarkup(
            visibleLines,
            lines.Count,
            viewportLineCount,
            startIndex,
            contentWidth);
    }

    private static List<ConversationLine> BuildConversationLines(
        AppState state,
        int contentWidth)
    {
        List<ConversationLine> lines = [];

        foreach (ChatMessage message in state.Messages)
        {
            AddMessageLines(lines, message, contentWidth);
        }

        return lines;
    }

    private static void AddMessageLines(
        List<ConversationLine> lines,
        ChatMessage message,
        int contentWidth)
    {
        string roleName = message.Role switch
        {
            Role.User => "You",
            Role.Assistant => "Nano",
            Role.System => "Nano",
            _ => "???"
        };

        string roleColor = message.Role switch
        {
            Role.User => "deepskyblue1",
            Role.Assistant => "mediumpurple1",
            Role.System => "yellow",
            _ => "grey"
        };

        string[] rawLines = message.Text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        bool firstLine = true;

        for (int lineIndex = 0; lineIndex < rawLines.Length; lineIndex++)
        {
            if (TryReadMarkdownTable(rawLines, lineIndex, out List<string[]> tableRows, out int consumedLineCount))
            {
                AddMarkdownTableLines(
                    lines,
                    tableRows,
                    ref firstLine,
                    roleName,
                    roleColor,
                    contentWidth);

                lineIndex += consumedLineCount - 1;
                continue;
            }

            AddMarkdownTextLine(
                lines,
                rawLines[lineIndex],
                ref firstLine,
                roleName,
                roleColor,
                contentWidth);
        }

        lines.Add(new ConversationLine(string.Empty, string.Empty));
    }

    private static string BuildScrollableConversationMarkup(
        IReadOnlyList<ConversationLine> visibleLines,
        int totalLineCount,
        int viewportLineCount,
        int startIndex,
        int contentWidth)
    {
        int thumbHeight = totalLineCount <= viewportLineCount
            ? viewportLineCount
            : Math.Clamp(
                (int)Math.Round(viewportLineCount * (viewportLineCount / (double)totalLineCount)),
                1,
                viewportLineCount);
        int thumbTop = 0;

        if (totalLineCount > viewportLineCount)
        {
            int maxStartIndex = Math.Max(1, totalLineCount - viewportLineCount);
            int maxThumbTop = Math.Max(0, viewportLineCount - thumbHeight);
            thumbTop = (int)Math.Round(startIndex / (double)maxStartIndex * maxThumbTop);
        }

        List<string> renderedLines = [];

        for (int index = 0; index < visibleLines.Count; index++)
        {
            ConversationLine line = visibleLines[index];
            string scrollGlyph = index >= thumbTop && index < thumbTop + thumbHeight
                ? "█"
                : "│";
            int spacerWidth = Math.Max(
                1,
                contentWidth + MessageScrollbarColumnWidth - line.Plain.Length - 1);

            renderedLines.Add(
                $"{line.Markup}{new string(' ', spacerWidth)}[grey]{scrollGlyph}[/]");
        }

        return string.Join('\n', renderedLines);
    }

    private static int GetMessageContentWidth()
    {
        return Math.Max(20, Console.WindowWidth - 8 - MessageScrollbarColumnWidth);
    }

    private static int GetMessageViewportLineCount(AppState state)
    {
        int reservedLines = HeaderPanelSize +
            (state.ActiveModal is null
                ? GetInputPanelSize(state) + 6
                : state.ActiveModal.PanelSize + GetInputPanelSize(state) + 7);

        return Math.Max(5, Console.WindowHeight - reservedLines);
    }

    private static int GetMaxConversationScrollOffset(AppState state)
    {
        int lineCount = BuildConversationLines(state, GetMessageContentWidth()).Count;
        return Math.Max(0, lineCount - GetMessageViewportLineCount(state));
    }

    private static void ScrollConversation(AppState state, int delta)
    {
        int maxScrollOffset = GetMaxConversationScrollOffset(state);
        state.ConversationScrollOffset = Math.Clamp(
            state.ConversationScrollOffset + delta,
            0,
            maxScrollOffset);
    }

    private static string BuildInputMarkup(AppState state)
    {
        if (state.ActiveModal is not null)
        {
            return state.ActiveModal.BuildInputMarkup();
        }

        return BuildInputLineMarkup(
            state.Input.ToString(),
            state.IsBusy || state.IsStreaming);
    }

    private static string BuildFooterMarkup(AppState state)
    {
        if (state.ActiveModal is not null)
        {
            string modalFooter = state.ActiveModal.BuildFooterMarkup();
            return BuildFooterLineMarkup(
                StripMarkup(modalFooter),
                modalFooter,
                BuildCompletionNote(state));
        }

        if (state.HasFatalError)
        {
            return BuildFooterLineMarkup(
                "Wheel/PgUp/PgDn: scroll  |  Esc/Ctrl+C: quit  |  /help",
                "[grey]Wheel/PgUp/PgDn: scroll[/]  [grey]|[/]  [grey]Esc/Ctrl+C: quit[/]  [grey]|[/]  [grey]/help[/]",
                BuildCompletionNote(state));
        }

        return BuildFooterLineMarkup(
            "Enter: send  |  Wheel/PgUp/PgDn: scroll  |  Esc/Ctrl+C: quit  |  /help",
            "[grey]Enter: send[/]  [grey]|[/]  [grey]Wheel/PgUp/PgDn: scroll[/]  [grey]|[/]  [grey]Esc/Ctrl+C: quit[/]  [grey]|[/]  [grey]/help[/]",
            BuildCompletionNote(state));
    }

    private static string BuildHeaderMarkup(AppState state)
    {
        (string Nano, string Agent)[] wordmark =
        [
            (
                "███╗   ██╗  █████╗  ███╗   ██╗  ██████╗",
                "  █████╗   ██████╗  ███████╗  ███╗   ██╗  ████████╗"
            ),
            (
                "████╗  ██║ ██╔══██╗ ████╗  ██║ ██╔═══██╗",
                " ██╔══██╗ ██╔════╝  ██╔════╝  ████╗  ██║  ╚══██╔══╝"
            ),
            (
                "██╔██╗ ██║ ███████║ ██╔██╗ ██║ ██║   ██║",
                " ███████║ ██║  ███╗ █████╗    ██╔██╗ ██║     ██║"
            ),
            (
                "██║╚██╗██║ ██╔══██║ ██║╚██╗██║ ██║   ██║",
                " ██╔══██║ ██║   ██║ ██╔══╝    ██║╚██╗██║     ██║"
            ),
            (
                "██║ ╚████║ ██║  ██║ ██║ ╚████║ ╚██████╔╝",
                " ██║  ██║ ╚██████╔╝ ███████╗  ██║ ╚████║     ██║"
            ),
            (
                "╚═╝  ╚═══╝ ╚═╝  ╚═╝ ╚═╝  ╚═══╝  ╚═════╝",
                "  ╚═╝  ╚═╝  ╚═════╝  ╚══════╝  ╚═╝  ╚═══╝     ╚═╝"
            )
        ];

        List<string> lines = [];

        for (int index = 0; index < wordmark.Length; index++)
        {
            string accentColor = index < 3 ? "fuchsia" : "purple";
            lines.Add(
                $"[grey]  [/][{accentColor}]   [/][white]{Markup.Escape(wordmark[index].Nano)}[/][fuchsia]{Markup.Escape(wordmark[index].Agent)}[/]");
        }

        lines.Add(string.Empty);
        lines.Add(
            $"[grey]  Sponsor:[/] [yellow]{Markup.Escape(SponsorName)}[/] [grey]([/][italic]{Markup.Escape(SponsorUrl)}[/][grey])[/]");
        lines.Add($"[grey]  {new string('-', HeaderDividerWidth)}[/]");
        lines.Add("[grey]  Chat in the terminal. Press Ctrl+C or use /exit to quit.[/]");
        lines.Add("[grey]  Press Esc while a response is running to interrupt the current request.[/]");

        return string.Join('\n', lines);
    }

    private static string BuildInputLineMarkup(
        string input,
        bool isBusy)
    {
        string normalizedInput = input ?? string.Empty;
        string busySuffixPlain = isBusy ? " (busy)" : string.Empty;
        string busySuffixMarkup = isBusy ? " [grey](busy)[/]" : string.Empty;
        const string promptPlain = "> ";
        const string promptMarkup = "[bold green]>[/] ";
        int contentWidth = Math.Max(20, Console.WindowWidth - 10);
        int maxInputLength = Math.Max(
            1,
            contentWidth - promptPlain.Length - busySuffixPlain.Length - InputCursorColumnWidth);
        IReadOnlyList<string> inputLines = WrapInputText(
            normalizedInput,
            maxInputLength,
            Math.Max(1, contentWidth - 2 - InputCursorColumnWidth));
        List<string> renderedLines = [];

        for (int index = 0; index < inputLines.Count; index++)
        {
            bool showPrompt = index == 0;
            bool showCursor = index == inputLines.Count - 1;
            string prefixMarkup = showPrompt ? promptMarkup : "  ";
            string suffixMarkup = showPrompt ? busySuffixMarkup : string.Empty;
            string cursorMarkup = showCursor ? BuildInputCursorMarkup() : string.Empty;

            renderedLines.Add($"{prefixMarkup}{Markup.Escape(inputLines[index])}{cursorMarkup}{suffixMarkup}");
        }

        return string.Join('\n', renderedLines);
    }

    private static int GetInputPanelSize(AppState state)
    {
        if (state.ActiveModal is not null)
        {
            return 4;
        }

        int bodyLineCount = WrapInputText(
                state.Input.ToString(),
                GetInputFirstLineTextWidth(state.IsBusy || state.IsStreaming),
                GetInputContinuationLineTextWidth()).Count;

        return Math.Max(3, bodyLineCount + 2);
    }

    private static int GetInputFirstLineTextWidth(bool isBusy)
    {
        const string promptPlain = "> ";
        string busySuffixPlain = isBusy ? " (busy)" : string.Empty;
        int contentWidth = Math.Max(20, Console.WindowWidth - 10);

        return Math.Max(
            1,
            contentWidth - promptPlain.Length - busySuffixPlain.Length - InputCursorColumnWidth);
    }

    private static int GetInputContinuationLineTextWidth()
    {
        int contentWidth = Math.Max(20, Console.WindowWidth - 10);
        return Math.Max(1, contentWidth - 2 - InputCursorColumnWidth);
    }

    internal static string BuildInputCursorMarkup()
    {
        long blinkFrame = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() /
            InputCursorBlinkIntervalMilliseconds;

        return blinkFrame % 2 == 0
            ? "[green]|[/]"
            : " ";
    }

    private static IReadOnlyList<string> WrapInputText(
        string input,
        int firstLineWidth,
        int continuationLineWidth)
    {
        string normalizedInput = input ?? string.Empty;
        if (normalizedInput.Length == 0)
        {
            return [string.Empty];
        }

        List<string> lines = [];
        int offset = 0;
        int width = Math.Max(1, firstLineWidth);

        while (offset < normalizedInput.Length)
        {
            int length = Math.Min(width, normalizedInput.Length - offset);
            lines.Add(normalizedInput.Substring(offset, length));
            offset += length;
            width = Math.Max(1, continuationLineWidth);
        }

        return lines;
    }

    private static string BuildCompletionNote(AppState state)
    {
        if (!string.IsNullOrWhiteSpace(state.PendingCompletionNote))
        {
            return state.PendingCompletionNote;
        }

        if (state.CurrentTurnStartedAt is null || (!state.IsBusy && !state.IsStreaming))
        {
            return DefaultCompletionNote;
        }

        TimeSpan elapsed = DateTimeOffset.UtcNow - state.CurrentTurnStartedAt.Value;
        int estimatedTokens = (int)Math.Floor(Math.Max(0d, elapsed.TotalSeconds) * EstimatedLiveTokensPerSecond);
        return FormatCompletionNote(elapsed, estimatedTokens);
    }

    private static string BuildFooterLineMarkup(
        string leftPlain,
        string leftMarkup,
        string? completionNote)
    {
        string plainCompletionNote = string.IsNullOrWhiteSpace(completionNote)
            ? DefaultCompletionNote
            : completionNote.Trim();

        const int minimumGap = 2;
        int contentWidth = Math.Max(20, Console.WindowWidth - 1);
        int maxCompletionNoteLength = contentWidth - leftPlain.Length - minimumGap;

        if (maxCompletionNoteLength <= 0)
        {
            return leftMarkup;
        }

        string displayCompletionNote = TruncateFromLeft(plainCompletionNote, maxCompletionNoteLength);
        int spacerWidth = Math.Max(
            minimumGap,
            contentWidth - leftPlain.Length - displayCompletionNote.Length);

        return $"{leftMarkup}{new string(' ', spacerWidth)}[grey]{Markup.Escape(displayCompletionNote)}[/]";
    }

    private static string FormatCompletionNote(
        TimeSpan elapsed,
        int estimatedTokens)
    {
        return $"({FormatMetricElapsed(elapsed)} · {FormatMetricTokens(estimatedTokens)} tokens)";
    }

    private static string FormatMetricElapsed(TimeSpan elapsed)
    {
        int seconds = Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds));
        TimeSpan normalized = TimeSpan.FromSeconds(seconds);

        if (normalized.TotalHours >= 1d)
        {
            return $"{(int)normalized.TotalHours}h {normalized.Minutes}m {normalized.Seconds}s";
        }

        if (normalized.TotalMinutes >= 1d)
        {
            return $"{(int)normalized.TotalMinutes}m {normalized.Seconds}s";
        }

        return $"{normalized.Seconds}s";
    }

    private static string FormatMetricTokens(int estimatedTokens)
    {
        int safeValue = Math.Max(0, estimatedTokens);
        if (safeValue < 1_000)
        {
            return safeValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        double thousands = safeValue / 1_000d;
        string format = thousands >= 10d ? "0" : "0.#";
        double rounded = Math.Round(
            thousands,
            thousands >= 10d ? 0 : 1,
            MidpointRounding.AwayFromZero);

        return $"{rounded.ToString(format, System.Globalization.CultureInfo.InvariantCulture)}k";
    }

    private static string StripMarkup(string markup)
    {
        return markup
            .Replace("[/]", string.Empty, StringComparison.Ordinal)
            .Replace("[grey]", string.Empty, StringComparison.Ordinal)
            .Replace("[bold]", string.Empty, StringComparison.Ordinal)
            .Replace("[yellow]", string.Empty, StringComparison.Ordinal)
            .Replace("[red]", string.Empty, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> WrapText(string value, int maxLineLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [string.Empty];
        }

        int safeMaxLineLength = Math.Max(1, maxLineLength);
        List<string> lines = [];

        for (int offset = 0; offset < value.Length; offset += safeMaxLineLength)
        {
            int length = Math.Min(safeMaxLineLength, value.Length - offset);
            lines.Add(value.Substring(offset, length));
        }

        return lines;
    }

    private static string TruncateFromLeft(string value, int maxLength)
    {
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[^maxLength..];
    }
}
