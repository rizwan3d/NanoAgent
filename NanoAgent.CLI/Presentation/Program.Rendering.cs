using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;

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
            root["body"].Update(BuildPromptPanel(state.ActiveModal));
        }
        else
        {
            root["body"].Update(BuildBodyPanel(state));
        }

        root["input"].Update(BuildInputPanel(state));
        root["footer"].Update(new Markup(BuildFooterMarkup(state)));

        return root;
    }

    private static IRenderable BuildHeader(AppState state)
    {
        string statusHeader =
            $"[bold cyan]NanoAgent[/]" +
            $" ── [grey]GitHub:[/] [deepskyblue1]{Markup.Escape(RepositoryUrl)} [/]";

        return new Panel(new Markup(BuildHeaderMarkup(state)))
            .Header(statusHeader)
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private static IRenderable BuildMessagesPanel(AppState state)
    {
        return new Panel(new Markup(BuildMessagesMarkup(state)))
            .Header($"[bold]Conversation[/] ──[grey] Working: {state.RootDirectory} [/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private static IRenderable BuildBodyPanel(AppState state)
    {
        if (!HasPinnedPlan(state))
        {
            return BuildMessagesPanel(state);
        }

        Layout body = new Layout("body")
            .SplitRows(
                new Layout("messages").Ratio(1),
                new Layout("plan").Size(GetPinnedPlanPanelSize(state)));

        body["messages"].Update(BuildMessagesPanel(state));
        body["plan"].Update(BuildPinnedPlanPanel(state));
        return body;
    }

    private static IRenderable BuildPinnedPlanPanel(AppState state)
    {
        return new Panel(new Markup(BuildPinnedPlanMarkup(state)))
            .Header("[bold cyan]Plan[/] [grey](F3 to hide)[/]")
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
            .Header(BuildInputPanelHeaderMarkup(state))
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private static string BuildInputPanelHeaderMarkup(AppState state)
    {
        string model = state.ActiveModelId ?? "n/a";
        string completionNote = BuildHeaderCompletionNote(state);
        const string plainPrefix = "Input -- Model: ";
        const int minimumNoteLength = 16;
        const int minimumSeparatorLength = 3;
        int headerBudget = Math.Max(24, Console.WindowWidth - 8);
        int modelBudget = Math.Max(
            3,
            headerBudget - plainPrefix.Length - minimumNoteLength - minimumSeparatorLength - 2);

        string displayModel = TruncateFromRight(model, modelBudget);
        int noteBudget = headerBudget -
            plainPrefix.Length -
            displayModel.Length -
            minimumSeparatorLength -
            2;
        string displayCompletionNote = noteBudget >= minimumNoteLength
            ? TruncateFromRight(completionNote, noteBudget)
            : string.Empty;
        string noteMarkup = string.IsNullOrWhiteSpace(displayCompletionNote)
            ? string.Empty
            : " " +
                $"{new string('─', Math.Max(
                    minimumSeparatorLength,
                    headerBudget - plainPrefix.Length - displayModel.Length - displayCompletionNote.Length - 2))}" +
                $" [grey]{Markup.Escape(displayCompletionNote)}[/]";

        return $"[bold green]Input[/] ── [grey]Model:[/] [aqua]{Markup.Escape(displayModel)}[/]{noteMarkup}";
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
                message.Role,
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

        if (state.ActiveModal is null && HasPinnedPlan(state))
        {
            reservedLines += GetPinnedPlanPanelSize(state);
        }

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

    private static bool HasPinnedPlan(AppState state)
    {
        return state.IsPlanPinned && !string.IsNullOrWhiteSpace(state.LatestPlanText);
    }

    private static int GetPinnedPlanPanelSize(AppState state)
    {
        int contentWidth = Math.Max(20, Console.WindowWidth - 8);
        int bodyLineCount = GetPinnedPlanLines(state)
            .Sum(line => WrapText(line, contentWidth).Count);
        int availableBodySize = Math.Max(
            5,
            Console.WindowHeight - HeaderPanelSize - GetInputPanelSize(state) - 1);
        int maxPanelSize = Math.Min(12, Math.Max(5, availableBodySize - 5));

        return Math.Clamp(bodyLineCount + 2, 5, maxPanelSize);
    }

    private static string BuildPinnedPlanMarkup(AppState state)
    {
        int contentWidth = Math.Max(20, Console.WindowWidth - 8);
        int maxBodyLines = Math.Max(1, GetPinnedPlanPanelSize(state) - 2);
        List<string> renderedLines = [];

        foreach (string line in GetPinnedPlanLines(state))
        {
            foreach (string wrappedLine in WrapText(line, contentWidth))
            {
                renderedLines.Add(FormatPinnedPlanLine(wrappedLine));
            }
        }

        if (renderedLines.Count > maxBodyLines)
        {
            renderedLines = renderedLines
                .Take(Math.Max(0, maxBodyLines - 1))
                .ToList();
            renderedLines.Add("[grey]...[/]");
        }

        return string.Join('\n', renderedLines);
    }

    private static string[] GetPinnedPlanLines(AppState state)
    {
        return (state.LatestPlanText ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static string FormatPinnedPlanLine(string line)
    {
        if (line.StartsWith("Plan progress:", StringComparison.Ordinal))
        {
            return $"[bold]{Markup.Escape(line)}[/]";
        }

        if (line.StartsWith("\u2713 ", StringComparison.Ordinal))
        {
            return $"[green]{Markup.Escape(line)}[/]";
        }

        if (line.StartsWith("\u2610 ", StringComparison.Ordinal))
        {
            return $"[grey]{Markup.Escape(line)}[/]";
        }

        return Markup.Escape(line);
    }

    private static string BuildInputMarkup(AppState state)
    {
        if (state.ActiveModal is not null)
        {
            return state.ActiveModal.BuildInputMarkup();
        }

        string input = state.Input.ToString();
        bool isBusy = state.IsBusy || state.IsStreaming;
        InputDisplayText inputDisplay = BuildInputDisplayText(
            input,
            state.InputCursorIndex,
            state.CollapsedInputPastes);

        if (inputDisplay.HasCollapsedPastes)
        {
            return BuildInputMarkupWithSuggestions(
                state,
                inputDisplay.Text,
                inputDisplay.CursorIndex,
                isBusy);
        }

        if (TryBuildLargeInputPasteMarkup(
            input,
            isBusy,
            out string largePasteMarkup,
            state))
        {
            return largePasteMarkup;
        }

        return BuildInputMarkupWithSuggestions(
            state,
            input,
            state.InputCursorIndex,
            isBusy);
    }

    private static string BuildInputMarkupWithSuggestions(
        AppState state,
        string input,
        int cursorIndex,
        bool isBusy)
    {
        string inputMarkup = BuildInputLineMarkup(
            input,
            cursorIndex,
            isBusy,
            state);

        if (TryGetSlashCommandSuggestions(state, out IReadOnlyList<SlashCommandSuggestion> suggestions))
        {
            return AppendInputPendingSummaries(inputMarkup, state) +
                "\n" +
                BuildSlashCommandSuggestionsMarkup(state, suggestions);
        }

        return AppendInputPendingSummaries(inputMarkup, state);
    }

    private static string AppendInputPendingSummaries(
        string inputMarkup,
        AppState state)
    {
        List<string> lines = [inputMarkup];

        if (TryBuildPastedTextSummary(state, out string pastedTextSummary))
        {
            lines.Add($"[grey]{Markup.Escape(pastedTextSummary)}[/]");
        }

        if (TryBuildInputAttachmentSummary(state, out string attachmentSummary))
        {
            lines.Add($"[grey]{Markup.Escape(attachmentSummary)}[/]");
        }

        return string.Join('\n', lines);
    }

    private static bool TryBuildPastedTextSummary(
        AppState state,
        out string summary)
    {
        List<CollapsedInputPaste> validPastes = state.CollapsedInputPastes
            .Where(paste => paste.Length > 0 &&
                paste.LineCount > MultilinePastePreviewLineThreshold &&
                paste.StartIndex >= 0 &&
                paste.StartIndex < state.Input.Length)
            .ToList();

        if (validPastes.Count > 0)
        {
            int totalLineCount = validPastes.Sum(static paste => paste.LineCount);
            string blockLabel = validPastes.Count == 1
                ? "1 pasted block"
                : $"{validPastes.Count} pasted blocks";
            string lineLabel = totalLineCount == 1
                ? "1 line"
                : $"{totalLineCount} lines";
            string hint = state.InputAttachments.Count > 0
                ? "Left/Right jumps block; Backspace/Delete removes at cursor"
                : "Left/Right jumps block; F4 removes nearest";
            summary = $"{blockLabel} ({lineLabel}) - {hint}";
            return true;
        }

        if (state.Input.Length > 0 &&
            TryGetLargePasteLineCount(state.Input.ToString(), out int lineCount))
        {
            string lineLabel = lineCount == 1
                ? "1 line"
                : $"{lineCount} lines";
            string hint = state.InputAttachments.Count > 0
                ? "remove by editing input"
                : "F4 removes it";
            summary = $"Pasted text ({lineLabel}) - {hint}";
            return true;
        }

        summary = string.Empty;
        return false;
    }

    private static bool TryBuildInputAttachmentSummary(
        AppState state,
        out string summary)
    {
        if (state.InputAttachments.Count == 0)
        {
            summary = string.Empty;
            return false;
        }

        int count = state.InputAttachments.Count;
        string label = count == 1
            ? $"1 file pasted/attached: {FormatAttachmentNames(state.InputAttachments)}"
            : $"{count} files pasted/attached: {FormatAttachmentNames(state.InputAttachments)}";
        string hint = " - F4 choose file";
        int contentWidth = Math.Max(20, Console.WindowWidth - 10);
        summary = TruncateFromRight(label, Math.Max(1, contentWidth - hint.Length)) + hint;
        return true;
    }

    private static string BuildFooterMarkup(AppState state)
    {
        if (state.ActiveModal is not null)
        {
            string modalFooter = state.ActiveModal.BuildFooterMarkup();
            return BuildFooterLineMarkup(modalFooter);
        }

        if (state.HasFatalError)
        {
            return BuildFooterLineMarkup(
                "[grey]Wheel/PgUp/PgDn: scroll[/]  [grey]|[/]  [grey]Ctrl+C: quit[/]  [grey]|[/]  [grey]/help[/]");
        }

        if (TryGetSlashCommandSuggestions(state, out _))
        {
            return BuildFooterLineMarkup(
                "[grey]Up/Down: select command[/]  [grey]|[/]  [grey]Enter: choose[/]  [grey]|[/]  [grey]Tab: complete[/]  [grey]|[/]  [grey]Esc: close[/]");
        }

        return BuildFooterLineMarkup(
            "[grey]Enter: Send[/]  [grey]|[/]  [grey]Shift+Enter: Newline[/]  [grey]|[/]  [grey]F2: Model[/]  [grey]|[/]  [grey]F3: Plan[/]  [grey]|[/]  [grey]F4: Files[/]  [grey]|[/]  [grey]Drop files to attach[/]  [grey]|[/]  [grey]Wheel/PgUp/PgDn: Scroll[/]  [grey]|[/]  [grey]Ctrl+C: quit[/]  [grey]|[/]  [grey]/help[/]");
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

        lines.Add(
            $"[grey]  Sponsor:[/] [yellow]{Markup.Escape(SponsorName)}[/] [grey]([/][italic]{Markup.Escape(SponsorUrl)}[/][grey])[/]");
        lines.Add("[grey]  Chat in the terminal. Press F2 to choose a model, F3 to pin the latest plan, Ctrl+C or /exit to quit. [/]");
        lines.Add("[grey]  [/]");

        return string.Join('\n', lines);
    }

    private static string BuildInputLineMarkup(
        string input,
        int cursorIndex,
        bool isBusy, AppState state)
    {
        string spinner = state.IsBusy || state.IsStreaming
            ? Spinner[state.SpinnerFrame / 4 % Spinner.Length]
            : " ";
        string normalizedInput = input ?? string.Empty;
        int normalizedCursorIndex = Math.Clamp(cursorIndex, 0, normalizedInput.Length);
        string busySuffixPlain = isBusy ? $"[yellow]{spinner}[/]" + " Press Esc to interrupt the current request." : string.Empty;
        string busySuffixMarkup = isBusy ? $"[yellow]{spinner}[/]" + " [grey]Press Esc to interrupt the current request.[/]" : string.Empty;
        const string promptPlain = "> ";
        const string promptMarkup = "[bold green]>[/] ";
        int contentWidth = Math.Max(20, Console.WindowWidth - 10);
        int maxInputLength = Math.Max(
            1,
            contentWidth - promptPlain.Length - busySuffixPlain.Length - InputCursorColumnWidth);
        IReadOnlyList<InputRenderLine> inputLines = WrapInputTextForCursor(
            normalizedInput,
            normalizedCursorIndex,
            maxInputLength,
            Math.Max(1, contentWidth - 2 - InputCursorColumnWidth));
        List<string> renderedLines = [];

        for (int index = 0; index < inputLines.Count; index++)
        {
            InputRenderLine inputLine = inputLines[index];
            bool showPrompt = index == 0;
            string prefixMarkup = showPrompt ? promptMarkup : "  ";
            string suffixMarkup = showPrompt ? busySuffixMarkup : string.Empty;
            string lineMarkup = BuildInputRenderLineMarkup(inputLine);

            renderedLines.Add($"{prefixMarkup}{lineMarkup}{suffixMarkup}");
        }

        return string.Join('\n', renderedLines);
    }

    private static string BuildInputRenderLineMarkup(InputRenderLine line)
    {
        if (line.CursorColumn is not int cursorColumn)
        {
            return Markup.Escape(line.Text);
        }

        int normalizedCursorColumn = Math.Clamp(cursorColumn, 0, line.Text.Length);
        string beforeCursor = line.Text[..normalizedCursorColumn];
        string afterCursor = line.Text[normalizedCursorColumn..];
        return Markup.Escape(beforeCursor) + BuildInputCursorMarkup() + Markup.Escape(afterCursor);
    }

    private static int GetInputPanelSize(AppState state)
    {
        if (state.ActiveModal is not null)
        {
            return 4;
        }

        string input = state.Input.ToString();
        bool isBusy = state.IsBusy || state.IsStreaming;
        InputDisplayText inputDisplay = BuildInputDisplayText(
            input,
            state.InputCursorIndex,
            state.CollapsedInputPastes);
        string visibleInput = inputDisplay.HasCollapsedPastes
            ? inputDisplay.Text
            : input;
        int bodyLineCount = !inputDisplay.HasCollapsedPastes &&
            GetInputLogicalLineCount(input) > MultilinePastePreviewLineThreshold
                ? 1
                : WrapInputText(
                        visibleInput,
                        GetInputFirstLineTextWidth(isBusy),
                        GetInputContinuationLineTextWidth()).Count;

        if (TryGetSlashCommandSuggestions(state, out IReadOnlyList<SlashCommandSuggestion> suggestions))
        {
            bodyLineCount += GetSlashCommandSuggestionLineCount(suggestions);
        }

        bodyLineCount += GetPendingInputSummaryLineCount(state);

        return Math.Max(3, bodyLineCount + 2);
    }

    private static int GetPendingInputSummaryLineCount(AppState state)
    {
        int lineCount = 0;

        if (TryBuildPastedTextSummary(state, out _))
        {
            lineCount++;
        }

        if (state.InputAttachments.Count > 0)
        {
            lineCount++;
        }

        return lineCount;
    }

    private static InputDisplayText BuildInputDisplayText(
        string input,
        int cursorIndex,
        IReadOnlyList<CollapsedInputPaste> collapsedPastes)
    {
        string normalizedInput = input ?? string.Empty;
        int normalizedCursorIndex = Math.Clamp(cursorIndex, 0, normalizedInput.Length);
        List<CollapsedInputPaste> validPastes = collapsedPastes
            .Where(paste => paste.Length > 0 &&
                paste.LineCount > MultilinePastePreviewLineThreshold &&
                paste.StartIndex >= 0 &&
                paste.StartIndex < normalizedInput.Length)
            .OrderBy(paste => paste.StartIndex)
            .ToList();

        if (validPastes.Count == 0)
        {
            return new InputDisplayText(
                normalizedInput,
                normalizedCursorIndex,
                HasCollapsedPastes: false);
        }

        StringBuilder display = new();
        int inputIndex = 0;
        int? displayCursorIndex = null;

        foreach (CollapsedInputPaste paste in validPastes)
        {
            int pasteStartIndex = Math.Clamp(paste.StartIndex, 0, normalizedInput.Length);
            int pasteEndIndex = Math.Clamp(paste.EndIndex, pasteStartIndex, normalizedInput.Length);
            if (pasteStartIndex < inputIndex)
            {
                continue;
            }

            AppendVisibleInputRange(
                normalizedInput,
                inputIndex,
                pasteStartIndex,
                normalizedCursorIndex,
                display,
                ref displayCursorIndex);

            string summary = BuildCollapsedPasteSummary(paste.LineCount);
            bool hasSuffix = pasteEndIndex < normalizedInput.Length;
            string separator = GetCollapsedPasteDisplaySeparator(
                normalizedInput,
                pasteStartIndex,
                pasteEndIndex,
                hasSuffix);

            if (displayCursorIndex is null &&
                normalizedCursorIndex > pasteStartIndex &&
                normalizedCursorIndex < pasteEndIndex)
            {
                displayCursorIndex = display.Length + summary.Length;
            }

            display.Append(summary);
            display.Append(separator);

            if (displayCursorIndex is null &&
                normalizedCursorIndex == pasteEndIndex)
            {
                displayCursorIndex = display.Length;
            }

            inputIndex = pasteEndIndex;
        }

        AppendVisibleInputRange(
            normalizedInput,
            inputIndex,
            normalizedInput.Length,
            normalizedCursorIndex,
            display,
            ref displayCursorIndex);

        return new InputDisplayText(
            display.ToString(),
            Math.Clamp(displayCursorIndex ?? display.Length, 0, display.Length),
            HasCollapsedPastes: true);
    }

    private static void AppendVisibleInputRange(
        string input,
        int startIndex,
        int endIndex,
        int cursorIndex,
        StringBuilder display,
        ref int? displayCursorIndex)
    {
        int safeStartIndex = Math.Clamp(startIndex, 0, input.Length);
        int safeEndIndex = Math.Clamp(endIndex, safeStartIndex, input.Length);

        if (displayCursorIndex is null &&
            cursorIndex >= safeStartIndex &&
            cursorIndex <= safeEndIndex)
        {
            displayCursorIndex = display.Length + cursorIndex - safeStartIndex;
        }

        display.Append(input, safeStartIndex, safeEndIndex - safeStartIndex);
    }

    private static string GetCollapsedPasteDisplaySeparator(
        string input,
        int pasteStartIndex,
        int pasteEndIndex,
        bool hasSuffix)
    {
        if (!hasSuffix ||
            pasteEndIndex <= pasteStartIndex)
        {
            return string.Empty;
        }

        if (input[pasteEndIndex] == '\n')
        {
            return string.Empty;
        }

        return input[pasteEndIndex - 1] == '\n'
            ? "\n"
            : " ";
    }

    private static bool TryBuildLargeInputPasteMarkup(
        string input,
        bool isBusy,
        out string markup,
        AppState state)
    {
        int lineCount = GetInputLogicalLineCount(input);
        if (lineCount <= MultilinePastePreviewLineThreshold)
        {
            markup = string.Empty;
            return false;
        }

        string summary = BuildCollapsedPasteSummary(lineCount);
        markup = BuildInputMarkupWithSuggestions(
            state,
            summary,
            summary.Length,
            isBusy);
        return true;
    }

    private static string BuildCollapsedPasteSummary(int lineCount)
    {
        string lineLabel = lineCount == 1 ? "line is" : "lines are";
        return $"{lineCount} {lineLabel} pasted";
    }

    private static int GetInputLogicalLineCount(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return 1;
        }

        string normalizedInput = input
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        string countableInput = normalizedInput.EndsWith('\n')
            ? normalizedInput[..^1]
            : normalizedInput;

        if (countableInput.Length == 0)
        {
            return 1;
        }

        return countableInput.Count(static character => character == '\n') + 1;
    }

    private static int GetInputFirstLineTextWidth(bool isBusy)
    {
        const string promptPlain = "> ";
        string busySuffixPlain = isBusy ? " Press Esc to interrupt the current request." : string.Empty;
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
        int width = Math.Max(1, firstLineWidth);
        string[] logicalLines = normalizedInput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (string logicalLine in logicalLines)
        {
            if (logicalLine.Length == 0)
            {
                lines.Add(string.Empty);
                width = Math.Max(1, continuationLineWidth);
                continue;
            }

            int offset = 0;
            while (offset < logicalLine.Length)
            {
                int length = Math.Min(width, logicalLine.Length - offset);
                lines.Add(logicalLine.Substring(offset, length));
                offset += length;
                width = Math.Max(1, continuationLineWidth);
            }

            width = Math.Max(1, continuationLineWidth);
        }

        return lines;
    }

    private static IReadOnlyList<InputRenderLine> WrapInputTextForCursor(
        string input,
        int cursorIndex,
        int firstLineWidth,
        int continuationLineWidth)
    {
        string normalizedInput = (input ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        int normalizedCursorIndex = Math.Clamp(cursorIndex, 0, normalizedInput.Length);
        List<InputRenderLine> lines = [];
        int width = Math.Max(1, firstLineWidth);
        int globalLineStart = 0;
        bool cursorRendered = false;
        string[] logicalLines = normalizedInput.Split('\n');

        for (int lineIndex = 0; lineIndex < logicalLines.Length; lineIndex++)
        {
            string logicalLine = logicalLines[lineIndex];

            if (logicalLine.Length == 0)
            {
                int? cursorColumn = !cursorRendered && normalizedCursorIndex == globalLineStart
                    ? 0
                    : null;
                cursorRendered |= cursorColumn is not null;
                lines.Add(new InputRenderLine(string.Empty, cursorColumn));
            }
            else
            {
                int offset = 0;
                while (offset < logicalLine.Length)
                {
                    int segmentStart = globalLineStart + offset;
                    int length = Math.Min(width, logicalLine.Length - offset);
                    int segmentEnd = segmentStart + length;
                    int? cursorColumn = null;

                    if (!cursorRendered &&
                        normalizedCursorIndex >= segmentStart &&
                        normalizedCursorIndex <= segmentEnd)
                    {
                        cursorColumn = normalizedCursorIndex - segmentStart;
                        cursorRendered = true;
                    }

                    lines.Add(new InputRenderLine(
                        logicalLine.Substring(offset, length),
                        cursorColumn));
                    offset += length;
                    width = Math.Max(1, continuationLineWidth);
                }
            }

            globalLineStart += logicalLine.Length;
            if (lineIndex < logicalLines.Length - 1)
            {
                globalLineStart++;
            }

            width = Math.Max(1, continuationLineWidth);
        }

        if (lines.Count == 0)
        {
            return [new InputRenderLine(string.Empty, 0)];
        }

        if (!cursorRendered)
        {
            InputRenderLine lastLine = lines[^1];
            lines[^1] = lastLine with { CursorColumn = lastLine.Text.Length };
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
            return FormatCompletionNote(
                TimeSpan.Zero,
                0,
                state.ActiveModelContextWindowTokens,
                0);
        }

        TimeSpan elapsed = DateTimeOffset.UtcNow - state.CurrentTurnStartedAt.Value;
        int elapsedSeconds = Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds));
        TimeSpan displayElapsed = TimeSpan.FromSeconds(elapsedSeconds);
        int estimatedTokens = (int)Math.Floor(elapsedSeconds * EstimatedLiveTokensPerSecond);
        return FormatCompletionNote(
            displayElapsed,
            estimatedTokens,
            state.ActiveModelContextWindowTokens,
            estimatedTokens);
    }

    private static string BuildHeaderCompletionNote(AppState state)
    {
        return BuildCompletionNote(state)
            .Trim()
            .Trim('[', ']')
            .Replace(" Used", " used", StringComparison.Ordinal)
            .Replace(" context", " ctx", StringComparison.Ordinal);
    }

    private static string BuildFooterLineMarkup(string markup)
    {
        return markup;
    }

    private static string FormatCompletionNote(
        TimeSpan elapsed,
        int estimatedTokens,
        int? contextWindowTokens,
        int? contextWindowUsedTokens)
    {
        string baseNote = $"{FormatMetricElapsed(elapsed)} · {FormatMetricTokens(estimatedTokens)} tokens";
        return contextWindowTokens is > 0
            ? $"[{baseNote} · {FormatContextWindowUsage(contextWindowUsedTokens ?? 0, contextWindowTokens.Value)} · {FormatContextWindowTokens(contextWindowTokens.Value)} context]"
            : $"[{baseNote}]";
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

    private static string FormatContextWindowUsage(
        int contextWindowUsedTokens,
        int contextWindowTokens)
    {
        int safeUsedTokens = Math.Max(0, contextWindowUsedTokens);
        int safeContextWindowTokens = Math.Max(1, contextWindowTokens);
        int percentage = (int)Math.Round(
            safeUsedTokens / (double)safeContextWindowTokens * 100d,
            MidpointRounding.AwayFromZero);

        return $"({percentage}%) {FormatMetricTokens(safeUsedTokens)} Used";
    }

    private static string FormatContextWindowTokens(int contextWindowTokens)
    {
        int safeValue = Math.Max(0, contextWindowTokens);
        if (safeValue < 1_000)
        {
            return safeValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (safeValue < 1_000_000)
        {
            return FormatScaledMetric(safeValue / 1_000d, "k");
        }

        return FormatScaledMetric(safeValue / 1_000_000d, "m");
    }

    private static string FormatScaledMetric(double value, string suffix)
    {
        string format = value >= 10d ? "0" : "0.#";
        double rounded = Math.Round(
            value,
            value >= 10d ? 0 : 1,
            MidpointRounding.AwayFromZero);

        return $"{rounded.ToString(format, System.Globalization.CultureInfo.InvariantCulture)}{suffix}";
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
}
