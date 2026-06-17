using NanoAgent.Application.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;

namespace NanoAgent.CLI;

public static partial class Program
{
    private const string MessagesPanelScrollHint = "[ Scroll: ↑/↓ | PgUp/PgDn | Wheel ]";
    private const string PlanPanelHideHint = "F3 to hide";
    private const string PlanPanelScrollHint = "Scroll: '[' up / ']' down";
    private const int FallbackWindowWidth = 80;
    private const int FallbackWindowHeight = 24;
    private const int MinimumPanelHeight = 3;
    private const int MinimumStandardLayoutHeight = 10;
    private const int PanelChromeLineCount = 2;
    private const int FooterPanelSize = 1;
    private const string BusyStatusText = "NanoAgent is working";

    private static IRenderable BuildUi(AppState state)
    {
        return BuildUi(state, GetWindowWidth(), GetWindowHeight());
    }

    private static IRenderable BuildUi(
        AppState state,
        int windowWidth,
        int windowHeight)
    {
        if (state.IsReaderViewActive)
        {
            return BuildReaderView(state);
        }

        if (windowWidth < 20 || windowHeight < MinimumStandardLayoutHeight)
        {
            return BuildCompactUi(state, windowWidth, windowHeight);
        }

        int footerSize = FooterPanelSize;
        int inputSize =Math.Max(MinimumPanelHeight, GetInputPanelSize(state));
        int headerSize = GetHeaderPanelSize(state);
        int bodyMinimumSize = HasPinnedPlan(state) ? 8 : MinimumPanelHeight;
        int totalFixedSize = headerSize + inputSize + footerSize;
        if (totalFixedSize + bodyMinimumSize > windowHeight)
        {
            int overflow = totalFixedSize + bodyMinimumSize - windowHeight;
            int reducibleHeader = Math.Max(0, headerSize - MinimumPanelHeight);
            int headerReduction = Math.Min(reducibleHeader, overflow);
            headerSize -= headerReduction;
            overflow -= headerReduction;

            if (overflow > 0)
            {
                int reducibleInput = Math.Max(0, inputSize - MinimumPanelHeight);
                int inputReduction = Math.Min(reducibleInput, overflow);
                inputSize -= inputReduction;
                overflow -= inputReduction;
            }

            if (overflow > 0)
            {
                return BuildCompactUi(state, windowWidth, windowHeight);
            }
        }

        Layout root = new Layout("root");
            root.SplitRows(
                new Layout("header").Size(headerSize),
                new Layout("body").Ratio(1),
                new Layout("input").Size(inputSize),
                new Layout("footer").Size(footerSize));

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

    private static IRenderable BuildCompactUi(
        AppState state,
        int windowWidth,
        int windowHeight)
    {
        string status = state.ActiveModal is null
            ? (state.IsBusy || state.IsStreaming ? "Busy" : "Ready")
            : "Action required";
        string model = string.IsNullOrWhiteSpace(state.ActiveModelId) ? "n/a" : state.ActiveModelId.ToDisplayName();
        string lastMessage = state.Messages.Count == 0
            ? "No messages yet."
            : state.Messages[^1].Text;
        int contentWidth = Math.Max(8, windowWidth - 2);
        string compactBody = string.Join(
            '\n',
            WrapText($"NanoAgent {status}", contentWidth)
                .Concat(WrapText($"Model: {model}", contentWidth))
                .Concat([string.Empty])
                .Concat(WrapText(lastMessage, contentWidth)));

        if (windowHeight < MinimumPanelHeight)
        {
            return new Markup(Markup.Escape(TruncateFromRight(
                compactBody.Replace('\n', ' '),
                Math.Max(1, contentWidth))));
        }

        return new Panel(new Markup(Markup.Escape(compactBody)))
            .Border(BoxBorder.Square)
            .Expand();
    }

    private static IRenderable BuildHeader(AppState state)
    {
        if (!state.HasMadeFirstLlmCall)
        {
            return new Panel(new Markup(CliBranding.BuildHeaderBodyMarkup()))
                .Header(CliBranding.BuildStatusHeaderMarkup())
                .Border(BoxBorder.Square)
                .Expand();
        }

        return new Panel(new Markup(CliBranding.BuildStatusHeaderMarkup()))
            .Border(BoxBorder.Square)
            .Expand();
    }

    private static IRenderable BuildMessagesPanel(AppState state)
    {
        return new Panel(BuildMessagesPanelContent(state))
            .Header(BuildMessagesPanelHeaderMarkup(state))
            .Border(BoxBorder.Square)
            .Expand();
    }

    private static IRenderable BuildMessagesPanelContent(AppState state)
    {
        if (!ShouldShowBusyStatus(state))
        {
            return new Markup(BuildMessagesMarkup(state));
        }

        Layout content = new Layout("messages-panel-content");
        content.SplitRows(
            new Layout("messages").Ratio(1),
            new Layout("busy").Size(GetBusyStatusReservedLineCount(state)));
        content["messages"].Update(new Markup(BuildMessagesMarkup(state)));
        content["busy"].Update(BuildBusyStatusRenderable(state));
        return content;
    }

    private static string BuildMessagesPanelHeaderMarkup(AppState state)
    {
        const string leftPrefix = "Session ── Working: ";
        int headerWidth = Math.Max(20, GetWindowWidth() - 6);
        int leftBudget = Math.Max(0, headerWidth - MessagesPanelScrollHint.Length - 1);
        string rootDirectory = state.RootDirectory ?? string.Empty;
        string displayRootDirectory = rootDirectory;
        string? gitBranch = GetGitBranchName(rootDirectory);
        string gitSuffix = gitBranch is not null ? $" [yellow]({Markup.Escape(gitBranch)})[/]" : string.Empty;

        if (leftPrefix.Length + displayRootDirectory.Length + gitSuffix.Length > leftBudget)
        {
            int rootBudget = Math.Max(0, leftBudget - leftPrefix.Length - gitSuffix.Length);
            displayRootDirectory = TruncateFromLeft(rootDirectory, rootBudget);
        }

        string leftPlain = leftPrefix + displayRootDirectory + gitSuffix;
        int spacerLength = Math.Max(1, headerWidth - leftPlain.Length - MessagesPanelScrollHint.Length);

        return $"[bold]Session[/] ──[grey] Working: {Markup.Escape(displayRootDirectory)}[/]{gitSuffix}" +
            $"{new string('─', spacerLength)}" +
            $"[grey]{Markup.Escape(MessagesPanelScrollHint)}[/]";
    }

    private static string? GetGitBranchName(string directory)
    {
        try
        {
            string? gitDir = FindGitDirectory(directory);
            if (gitDir is null) return null;

            string headFile = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headFile)) return null;

            string head = File.ReadAllText(headFile).Trim();
            const string refPrefix = "ref: refs/heads/";
            if (head.StartsWith(refPrefix, StringComparison.Ordinal))
            {
                return head[refPrefix.Length..];
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindGitDirectory(string directory)
    {
        var dir = new DirectoryInfo(directory);
        while (dir is not null)
        {
            string gitDir = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitDir) || File.Exists(gitDir))
                return gitDir;
            dir = dir.Parent;
        }
        return null;
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
            .Header($"[bold cyan]Plan[/] [grey]({PlanPanelHideHint})[/]")
            .Header(BuildPinnedPlanPanelHeaderMarkup(state))
            .Border(BoxBorder.Square)
            .Expand();
    }

    private static string BuildPinnedPlanPanelHeaderMarkup(AppState state)
    {
        const string leftPlain = "Plan ";
        int headerWidth = Math.Max(20, GetWindowWidth() - 6);
        string rightPlain = BuildPinnedPlanPanelHeaderInfo(state);
        int rightBudget = Math.Max(0, headerWidth - leftPlain.Length - 1);
        string displayRight = TruncateFromRight(rightPlain, rightBudget);
        int spacerLength = Math.Max(
            1,
            headerWidth - leftPlain.Length - displayRight.Length);

        return $"[bold cyan]Plan[/] {new string('─', spacerLength)}[grey]{Markup.Escape(displayRight)}[/]";
    }

    private static string BuildPinnedPlanPanelHeaderInfo(AppState state)
    {
        int totalLineCount = GetPinnedPlanRenderedLineCount(state);
        int viewportLineCount = GetPinnedPlanViewportLineCount(state);
        int maxScrollOffset = Math.Max(0, totalLineCount - viewportLineCount);

        if (totalLineCount <= viewportLineCount)
        {
            return PlanPanelHideHint;
        }

        int startLine = Math.Clamp(state.PlanScrollOffset, 0, maxScrollOffset) + 1;
        int endLine = Math.Min(
            totalLineCount,
            startLine + viewportLineCount - 1);

        return $"{PlanPanelScrollHint} · {startLine}-{endLine}/{totalLineCount} · {PlanPanelHideHint}";
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
            .Border(BoxBorder.Square)
            .Expand();
    }

    private static string BuildInputPanelHeaderMarkup(AppState state)
    {
        string rawModel = state.ActiveModelId ?? "n/a";
        string modelName = rawModel.ToDisplayName();
        string completionNote = BuildHeaderCompletionNote(state);
        const string plainPrefix = "Input -- Model: ";
        const int minimumNoteLength = 16;
        const int minimumSeparatorLength = 3;
        int headerBudget = Math.Max(24, GetWindowWidth() - 8);
        int modelBudget = Math.Max(
            3,
            headerBudget - plainPrefix.Length - minimumNoteLength - minimumSeparatorLength - 2);

        string plainModel = modelName;
        string markupModel = Markup.Escape(modelName);
        if (!string.IsNullOrWhiteSpace(state.ProviderName))
        {
            string plainProvider = $" ({state.ProviderName})";
            string markupProvider = $" [grey]{Markup.Escape(state.ProviderName)}[/]";
            if (plainModel.Length + plainProvider.Length <= modelBudget)
            {
                plainModel += plainProvider;
                markupModel += markupProvider;
            }
        }

        if (!string.IsNullOrWhiteSpace(state.ReasoningEffort))
        {
            string plainEffort = $" ·{state.ReasoningEffort}";
            string markupEffort = $" ·[green]{Markup.Escape(state.ReasoningEffort)}[/]";
            if (plainModel.Length + plainEffort.Length <= modelBudget)
            {
                plainModel += plainEffort;
                markupModel += markupEffort;
            }
        }

        string displayPlainModel = TruncateFromRight(plainModel, modelBudget);
        string displayMarkupModel = TruncateFromRight(markupModel, modelBudget);
        int noteBudget = headerBudget -
            plainPrefix.Length -
            displayPlainModel.Length -
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
                    headerBudget - plainPrefix.Length - displayPlainModel.Length - displayCompletionNote.Length - 2))}" +
                $" [grey]{Markup.Escape(displayCompletionNote)}[/]";

        return $"[bold green]Input[/] ── [grey]Model:[/] [aqua]{displayMarkupModel}[/]{noteMarkup}";
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

        if (state.IsCopyModeActive)
        {
            state.CopyCursorLine = Math.Clamp(state.CopyCursorLine, 0, lines.Count - 1);
            EnsureCopyCursorVisible(state, lines.Count, viewportLineCount);
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

        if (state.IsCopyModeActive)
        {
            visibleLines = ApplyCopyModeHighlight(state, visibleLines, startIndex);
        }

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
            Role.User => "❯",
            Role.Assistant => "◆",
            Role.Thinking => "◇",
            Role.System => "◆",
            _ => "?"
        };

        string roleColor = message.Role switch
        {
            Role.User => "deepskyblue1",
            Role.Assistant => "mediumpurple1",
            Role.Thinking => "grey58",
            Role.System => "yellow",
            _ => "grey"
        };

        string[] rawLines = message.Text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        bool firstLine = true;
        ToolOutputHighlightContext toolOutputContext = default;

        for (int lineIndex = 0; lineIndex < rawLines.Length; lineIndex++)
        {
            if (TryReadFencedCodeBlock(
                rawLines,
                lineIndex,
                out string? codeLanguage,
                out List<string> codeBlockLines,
                out int codeConsumedLineCount))
            {
                AddCodeBlockLines(
                    lines,
                    codeLanguage,
                    codeBlockLines,
                    ref firstLine,
                    roleName,
                    roleColor,
                    contentWidth);

                lineIndex += codeConsumedLineCount - 1;
                continue;
            }

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

            if (message.Role == Role.System)
            {
                UpdateToolOutputHighlightContext(rawLines[lineIndex], ref toolOutputContext);

                if (TryAddHighlightedToolOutputLine(
                        lines,
                        rawLines[lineIndex],
                        toolOutputContext,
                        ref firstLine,
                        roleName,
                        roleColor,
                        contentWidth))
                {
                    continue;
                }
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
        return Math.Max(20, GetWindowWidth() - 8 - MessageScrollbarColumnWidth);
    }

    private static int GetMessageViewportLineCount(AppState state)
    {
        if (state.ActiveModal is not null)
        {
            return 0;
        }

        int messagesPanelSize = GetMessagesPanelSize(state);
        int reservedLines = PanelChromeLineCount + GetBusyStatusReservedLineCount(state);
        return Math.Max(1, messagesPanelSize - reservedLines);
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
        int contentWidth = GetPinnedPlanContentWidth();
        int bodyLineCount = GetPinnedPlanLines(state)
            .Sum(line => WrapText(line, contentWidth).Count);
        int inputSize = GetInputPanelSize(state);
        int availableBodySize = Math.Max(
            5,
            GetWindowHeight() - GetHeaderPanelSize(state) - inputSize - FooterPanelSize);
        int maxPanelSize = Math.Min(12, Math.Max(5, availableBodySize - 5));

        return Math.Clamp(bodyLineCount + 2, 5, maxPanelSize);
    }

    private static int GetMessagesPanelSize(AppState state)
    {
        int bodySize = Math.Max(
            MinimumPanelHeight,
            GetWindowHeight() - GetHeaderPanelSize(state) - GetInputPanelSize(state) - FooterPanelSize);

        if (HasPinnedPlan(state))
        {
            bodySize = Math.Max(MinimumPanelHeight, bodySize - GetPinnedPlanPanelSize(state));
        }

        return bodySize;
    }

    private static int GetBusyStatusReservedLineCount(AppState state)
    {
        return ShouldShowBusyStatus(state) ? 1 : 0;
    }

    private static bool ShouldShowBusyStatus(AppState state)
    {
        return state.ActiveModal is null && (state.IsBusy || state.IsStreaming);
    }

    private static IRenderable BuildBusyStatusRenderable(AppState state)
    {
        string spinner = Spinner[state.SpinnerFrame / 4 % Spinner.Length];
        string busyStatusText = BuildBusyStatusText(state);
        StringBuilder markup = new();
        markup.Append("[bold aqua]")
            .Append(Markup.Escape($"{spinner} "))
            .Append("[/]");

        double animationTime = state.SpinnerFrame / 8d;
        for (int index = 0; index < busyStatusText.Length; index++)
        {
            char character = busyStatusText[index];
            int red = 0;
            int green = Math.Clamp(
                (int)Math.Round(120 + 100 * Math.Sin(animationTime * 0.6d + index * 0.6d)),
                80,
                255);
            int blue = Math.Clamp(
                (int)Math.Round(200 + 55 * Math.Sin(animationTime * 0.6d + index * 0.6d + 1d)),
                160,
                255);

            markup.Append($"[bold #{red:X2}{green:X2}{blue:X2}]")
                .Append(Markup.Escape(character.ToString()))
                .Append("[/]");
        }

        return new Markup(markup.ToString());
    }

    private static string BuildBusyStatusText(AppState state)
    {
        return state.PendingSubmissions.Count == 0
            ? BusyStatusText
            : $"{BusyStatusText} - {state.PendingSubmissions.Count} queued";
    }

    private static int GetPinnedPlanContentWidth()
    {
        return Math.Max(20, GetWindowWidth() - 8);
    }

    private static int GetPinnedPlanViewportLineCount(AppState state)
    {
        return Math.Max(1, GetPinnedPlanPanelSize(state) - 2);
    }

    private static int GetPinnedPlanRenderedLineCount(AppState state)
    {
        return BuildPinnedPlanRenderableLines(
            state,
            GetPinnedPlanContentWidth()).Count;
    }

    private static int GetMaxPinnedPlanScrollOffset(AppState state)
    {
        int lineCount = GetPinnedPlanRenderedLineCount(state);
        return Math.Max(0, lineCount - GetPinnedPlanViewportLineCount(state));
    }

    private static void ScrollPinnedPlan(AppState state, int delta)
    {
        if (!HasPinnedPlan(state))
        {
            return;
        }

        int maxScrollOffset = GetMaxPinnedPlanScrollOffset(state);
        state.PlanScrollOffset = Math.Clamp(
            state.PlanScrollOffset + delta,
            0,
            maxScrollOffset);
    }

    private static string BuildPinnedPlanMarkup(AppState state)
    {

        int contentWidth = GetPinnedPlanContentWidth();
        int viewportLineCount = GetPinnedPlanViewportLineCount(state);
        List<ConversationLine> renderedLines = BuildPinnedPlanRenderableLines(
            state,
            contentWidth);
        int maxScrollOffset = Math.Max(0, renderedLines.Count - viewportLineCount);
        state.PlanScrollOffset = Math.Clamp(
            state.PlanScrollOffset,
            0,
            maxScrollOffset);

        List<ConversationLine> visibleLines = renderedLines
            .Skip(state.PlanScrollOffset)
            .Take(viewportLineCount)
            .ToList();

        while (visibleLines.Count < viewportLineCount)
        {
            visibleLines.Add(new ConversationLine(string.Empty, string.Empty));
        }

        return string.Join('\n', visibleLines.Select(static line => line.Markup));
    }

    private static List<ConversationLine> BuildPinnedPlanRenderableLines(
        AppState state,
        int contentWidth)
    {
        List<ConversationLine> renderedLines = [];

        foreach (string line in GetPinnedPlanLines(state))
        {
            foreach (string wrappedLine in WrapText(line, contentWidth))
            {
                renderedLines.Add(new ConversationLine(
                    FormatPinnedPlanLine(wrappedLine),
                    wrappedLine));
            }
        }

        if (renderedLines.Count == 0)
        {
            renderedLines.Add(new ConversationLine(string.Empty, string.Empty));
        }

        return renderedLines;
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
            cursorIndex);

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

        if (TryBuildPendingSubmissionSummary(state, out string pendingSubmissionSummary))
        {
            lines.Add($"[grey]{Markup.Escape(pendingSubmissionSummary)}[/]");
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
        int contentWidth = Math.Max(20, GetWindowWidth() - 10);
        summary = TruncateFromRight(label, Math.Max(1, contentWidth - hint.Length)) + hint;
        return true;
    }

    private static bool TryBuildPendingSubmissionSummary(
        AppState state,
        out string summary)
    {
        if (state.PendingSubmissions.Count == 0)
        {
            summary = string.Empty;
            return false;
        }

        int queuedCount = state.PendingSubmissions.Count;
        summary = queuedCount == 1
            ? "1 queued prompt - F4 removes newest"
            : $"{queuedCount} queued prompts - F4 removes newest";
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
                "[grey]Ctrl+C: quit[/]  [grey]|[/]  [grey]/help[/]");
        }

        if (state.IsReaderViewActive)
        {
            return BuildFooterLineMarkup(
                "[grey]Reader view: drag-select with the mouse to copy[/]  [grey]|[/]  [grey]↑/↓ PgUp/PgDn: scroll[/]  [grey]|[/]  [grey]Esc/F5: exit[/]");
        }

        if (state.IsCopyModeActive)
        {
            return BuildFooterLineMarkup(
                "[grey]Copy mode: ↑/↓ move[/]  [grey]|[/]  [grey]V/Space: start selection[/]  [grey]|[/]  [grey]Shift+↑/↓: extend[/]  [grey]|[/]  [grey]A: all[/]  [grey]|[/]  [grey]Y: copy[/]  [grey]|[/]  [grey]Esc: exit[/]");
        }

        if (state.IsBusy || state.IsStreaming)
        {
            return BuildFooterLineMarkup(
                "[grey]Enter: queue prompt[/]  [grey]|[/]  [grey]F4: remove queued[/]  [grey]|[/]  [grey]Esc: interrupt[/]  [grey]|[/]  [grey]F3: Plan[/]  [grey]|[/]  [grey]F5: Reader[/]  [grey]|[/]  [grey]F6: Copy[/]  [grey]|[/]  [grey]Ctrl+C: quit[/]  [grey]|[/]  [grey]/help[/]");
        }

        if (TryGetSlashCommandSuggestions(state, out _))
        {
            return BuildFooterLineMarkup(
                "[grey]Up/Down: select command[/]  [grey]|[/]  [grey]Enter: choose[/]  [grey]|[/]  [grey]Tab: complete[/]  [grey]|[/]  [grey]Esc: close[/]");
        }

        return BuildFooterLineMarkup(
            "[grey]Enter: Send[/]  [grey]|[/]  [grey]Shift+Enter: Newline[/]  [grey]|[/]  [grey]F2: Model[/]  [grey]|[/]  [grey]F3: Plan[/]  [grey]|[/]  [grey]F4: Files[/]  [grey]|[/]  [grey]F5: Reader[/]  [grey]|[/]  [grey]F6: Copy[/]  [grey]|[/]  [grey]Ctrl+C: quit[/]  [grey]|[/]  [grey]/help[/]");
    }

    private static string BuildInputLineMarkup(
        string input,
        int cursorIndex)
    {
        string normalizedInput = input ?? string.Empty;
        int normalizedCursorIndex = Math.Clamp(cursorIndex, 0, normalizedInput.Length);
        const string promptPlain = "> ";
        const string promptMarkup = "[bold green]❯[/] ";
        int contentWidth = Math.Max(20, GetWindowWidth() - 10);
        int maxInputLength = Math.Max(
            1,
            contentWidth - promptPlain.Length - InputCursorColumnWidth);
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
            string lineMarkup = BuildInputRenderLineMarkup(inputLine);

            renderedLines.Add($"{prefixMarkup}{lineMarkup}");
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
                        GetInputFirstLineTextWidth(),
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

        if (state.PendingSubmissions.Count > 0)
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

    private static int GetInputFirstLineTextWidth()
    {
        const string promptPlain = "❯ ";
        int contentWidth = Math.Max(20, GetWindowWidth() - 10);

        return Math.Max(
            1,
            contentWidth - promptPlain.Length - InputCursorColumnWidth);
    }

    private static int GetInputContinuationLineTextWidth()
    {
        int contentWidth = Math.Max(20, GetWindowWidth() - 10);
        return Math.Max(1, contentWidth - 2 - InputCursorColumnWidth);
    }

    private static int GetWindowWidth()
    {
        try
        {
            return Console.WindowWidth > 0
                ? Console.WindowWidth
                : FallbackWindowWidth;
        }
        catch (IOException)
        {
            return FallbackWindowWidth;
        }
    }

    private static int GetWindowHeight()
    {
        try
        {
            return Console.WindowHeight > 0
                ? Console.WindowHeight
                : FallbackWindowHeight;
        }
        catch (IOException)
        {
            return FallbackWindowHeight;
        }
    }

    internal static string BuildInputCursorMarkup()
    {
        long blinkFrame = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() /
            InputCursorBlinkIntervalMilliseconds;

        return blinkFrame % 2 == 0
            ? "[green]█[/]"
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
            .Trim();
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

        return maxLength <= 3
            ? value[^maxLength..]
            : "..." + value[^(maxLength - 3)..];
    }
}
