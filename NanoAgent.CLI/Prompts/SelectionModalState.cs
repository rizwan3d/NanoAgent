using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using Spectre.Console;

namespace NanoAgent.CLI;

public sealed class SelectionModalState<T> : UiModalState
{
    private const int MinimumPanelSize = 7;
    private const int PanelChromeLineCount = 2;
    private const int PanelHorizontalPadding = 6;
    private const int ReservedLayoutLineCount = 15;
    private const int MaxDescriptionLineCount = 8;
    private const int FallbackWindowWidth = 80;
    private const int FallbackWindowHeight = 24;

    private readonly Action<Exception>? _onCancelled;
    private readonly bool _descriptionSupportsMarkup;
    private readonly Action<T> _onSelected;
    private readonly IReadOnlyList<SelectionPromptOption<T>> _options;
    private int?[] _visibleOptionIndexes = [];

    private SelectionModalState(
        SelectionPromptRequest<T> request,
        object completionToken,
        Action<T> onSelected,
        Action<Exception>? onCancelled)
        : base(
            request.Title,
            request.Description,
            request.AllowCancellation,
            request.AutoSelectAfter,
            completionToken)
    {
        _options = request.Options;
        _descriptionSupportsMarkup = request.DescriptionSupportsMarkup;
        _onSelected = onSelected;
        _onCancelled = onCancelled;
        SelectedIndex = Math.Clamp(request.DefaultIndex, 0, Math.Max(0, _options.Count - 1));
    }

    public int SelectedIndex { get; private set; }

    public override int PanelSize
    {
        get
        {
            int contentWidth = Math.Max(20, GetWindowWidth() - PanelHorizontalPadding);
            int bodyLineCount = CountWrappedLines(Title, contentWidth);

            if (!string.IsNullOrWhiteSpace(Description))
            {
                bodyLineCount += 1 + GetVisibleDescriptionLines(contentWidth).Count;
            }

            if (DeadlineUtc is not null)
            {
                bodyLineCount += 2;
            }

            bodyLineCount++;

            for (int index = 0; index < _options.Count; index++)
            {
                SelectionPromptOption<T> option = _options[index];
                if (ShouldRenderSectionHeader(index))
                {
                    bodyLineCount += (index > 0 ? 1 : 0) + CountWrappedLines(option.Section, contentWidth);
                }

                bodyLineCount += CountWrappedLines($"> {index + 1}. {option.Label}", contentWidth);

                if (!string.IsNullOrWhiteSpace(option.Description))
                {
                    bodyLineCount += CountWrappedLines($"    {option.Description}", contentWidth);
                }
            }

            int requestedSize = bodyLineCount + PanelChromeLineCount;
            int maxPanelSize = Math.Max(
                PanelChromeLineCount + 1,
                GetWindowHeight() - ReservedLayoutLineCount);
            int minPanelSize = Math.Min(MinimumPanelSize, maxPanelSize);

            return Math.Clamp(requestedSize, minPanelSize, maxPanelSize);
        }
    }

    public static SelectionModalState<T> Create(
        SelectionPromptRequest<T> request,
        object completionToken,
        Action<T> onSelected,
        Action<Exception>? onCancelled = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(completionToken);
        ArgumentNullException.ThrowIfNull(onSelected);

        return new SelectionModalState<T>(
            request,
            completionToken,
            onSelected,
            onCancelled);
    }

    public override string BuildBodyMarkup()
    {
        int contentWidth = GetContentWidth();
        int maxBodyLines = Math.Max(1, PanelSize - PanelChromeLineCount);
        List<SelectionRenderLine> headingLines = BuildHeadingLines(contentWidth);
        List<SelectionRenderLine> optionLines = BuildOptionLines(contentWidth);

        if (headingLines.Count >= maxBodyLines)
        {
            if (maxBodyLines == 1)
            {
                headingLines = [];
            }
            else
            {
                headingLines = headingLines
                    .Take(Math.Max(0, maxBodyLines - 2))
                    .Append(new SelectionRenderLine("[grey]...[/]"))
                    .ToList();
            }
        }

        int availableOptionLines = maxBodyLines - headingLines.Count;
        IReadOnlyList<SelectionRenderLine> visibleOptionLines = GetVisibleOptionLines(
            optionLines,
            availableOptionLines);

        List<SelectionRenderLine> visibleLines = headingLines
            .Concat(visibleOptionLines)
            .ToList();
        _visibleOptionIndexes = visibleLines
            .Select(static line => line.OptionIndex)
            .ToArray();

        return string.Join('\n', visibleLines.Select(static line => line.Markup)).TrimEnd();
    }

    public override string BuildFooterMarkup()
    {
        return AllowCancellation
            ? "[grey]Up/Down or hover: select[/]  [grey]|[/]  [grey]Enter/click: confirm[/]  [grey]|[/]  [grey]Esc: cancel[/]"
            : "[grey]Up/Down or hover: select[/]  [grey]|[/]  [grey]Enter/click: confirm[/]";
    }

    public override string BuildInputMarkup()
    {
        if (DeadlineUtc is not null)
        {
            return $"[yellow]Selection prompt active.[/] Use [bold]Up/Down[/], hover, or click. Auto-select in [red]{GetRemainingSeconds()}s[/].";
        }

        return "[yellow]Selection prompt active.[/] Use [bold]Up/Down[/], hover, or click.";
    }

    public override void HandleKey(AppState state, ConsoleKeyInfo key)
    {
        if (_options.Count == 0)
        {
            Cancel(state);
            return;
        }

        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.LeftArrow)
        {
            MoveSelection(-1);
            return;
        }

        if (key.Key is ConsoleKey.DownArrow or ConsoleKey.RightArrow)
        {
            MoveSelection(1);
            return;
        }

        if (key.Key == ConsoleKey.Enter ||
            key.KeyChar is '\r' or '\n')
        {
            Resolve(state, _options[SelectedIndex].Value);
            return;
        }

        if (IsCancellationKey(key) && AllowCancellation)
        {
            Cancel(state);
            return;
        }

        if (char.IsDigit(key.KeyChar))
        {
            int index = key.KeyChar - '1';
            if (index >= 0 && index < _options.Count)
            {
                SelectedIndex = index;
            }
        }
    }

    public override void HandleMouse(
        AppState state,
        int column,
        int row,
        int buttonCode,
        bool isPress)
    {
        if (_options.Count == 0)
        {
            return;
        }

        if (column < state.ModalContentLeftColumn ||
            column > state.ModalContentRightColumn ||
            row < state.ModalContentTopRow ||
            row > state.ModalContentBottomRow)
        {
            return;
        }

        int normalizedButtonCode = buttonCode & ~0b1_1100;
        if (normalizedButtonCode == 64)
        {
            MoveSelection(-1);
            return;
        }

        if (normalizedButtonCode == 65)
        {
            MoveSelection(1);
            return;
        }

        int lineIndex = row - state.ModalContentTopRow;
        if (lineIndex < 0 || lineIndex >= _visibleOptionIndexes.Length)
        {
            return;
        }

        int? optionIndex = _visibleOptionIndexes[lineIndex];
        if (optionIndex is not int hoveredIndex ||
            hoveredIndex < 0 ||
            hoveredIndex >= _options.Count)
        {
            return;
        }

        SelectedIndex = hoveredIndex;

        if (isPress && normalizedButtonCode == 0)
        {
            Resolve(state, _options[SelectedIndex].Value);
        }
    }

    protected override void ResolveByTimeout(AppState state)
    {
        if (_options.Count == 0)
        {
            Cancel(state);
            return;
        }

        Resolve(state, _options[SelectedIndex].Value);
    }

    private void Cancel(AppState state)
    {
        state.ActiveModal = null;
        _onCancelled?.Invoke(new PromptCancelledException());
        Program.TryStartNextPendingSubmission(state);
    }

    private void MoveSelection(int delta)
    {
        if (_options.Count == 0)
        {
            return;
        }

        int nextIndex = SelectedIndex + delta;

        if (nextIndex < 0)
        {
            nextIndex = _options.Count - 1;
        }
        else if (nextIndex >= _options.Count)
        {
            nextIndex = 0;
        }

        SelectedIndex = nextIndex;
    }

    private void Resolve(AppState state, T value)
    {
        state.ActiveModal = null;
        _onSelected(value);
        Program.TryStartNextPendingSubmission(state);
    }

    private List<SelectionRenderLine> BuildHeadingLines(int contentWidth)
    {
        List<SelectionRenderLine> lines = [];
        lines.AddRange(WrapMarkupLines(Title, contentWidth, "[bold yellow]", "[/]")
            .Select(static line => new SelectionRenderLine(line)));

        if (!string.IsNullOrWhiteSpace(Description))
        {
            lines.Add(new SelectionRenderLine(string.Empty));
            lines.AddRange(GetVisibleDescriptionMarkupLines(contentWidth)
                .Select(static line => new SelectionRenderLine(line)));
        }

        if (DeadlineUtc is not null)
        {
            lines.Add(new SelectionRenderLine(string.Empty));
            lines.Add(new SelectionRenderLine($"[grey]Auto-select in {GetRemainingSeconds()}s[/]"));
        }

        lines.Add(new SelectionRenderLine(string.Empty));
        return lines;
    }

    private List<SelectionRenderLine> BuildOptionLines(int contentWidth)
    {
        List<SelectionRenderLine> lines = [];

        for (int index = 0; index < _options.Count; index++)
        {
            SelectionPromptOption<T> option = _options[index];
            if (ShouldRenderSectionHeader(index))
            {
                if (lines.Count > 0)
                {
                    lines.Add(new SelectionRenderLine(string.Empty));
                }

                foreach (string sectionLine in WrapPlainText(option.Section, Math.Max(1, contentWidth)))
                {
                    lines.Add(new SelectionRenderLine($"[bold grey]{Markup.Escape(sectionLine)}[/]"));
                }
            }

            bool selected = index == SelectedIndex;
            string firstPrefix = selected ? "> " : "  ";
            string continuationPrefix = selected ? "  " : "  ";
            string label = $"{index + 1}. {option.Label}";
            int labelWidth = Math.Max(1, contentWidth - firstPrefix.Length);
            int labelContinuationWidth = Math.Max(1, contentWidth - continuationPrefix.Length);
            IReadOnlyList<string> labelLines = WrapPlainText(label, labelWidth, labelContinuationWidth);

            for (int lineIndex = 0; lineIndex < labelLines.Count; lineIndex++)
            {
                string prefix = lineIndex == 0
                    ? firstPrefix
                    : continuationPrefix;
                string line = prefix + Markup.Escape(labelLines[lineIndex]);
                lines.Add(new SelectionRenderLine(
                    selected
                        ? $"[black on green]{line}[/]"
                        : $"[green]{line}[/]",
                    index));
            }

            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                foreach (string descriptionLine in WrapPlainText(option.Description, Math.Max(1, contentWidth - 4)))
                {
                    lines.Add(new SelectionRenderLine($"[grey]    {Markup.Escape(descriptionLine)}[/]", index));
                }
            }
        }

        return lines;
    }

    private IReadOnlyList<SelectionRenderLine> GetVisibleOptionLines(
        IReadOnlyList<SelectionRenderLine> optionLines,
        int availableLineCount)
    {
        if (availableLineCount <= 0 ||
            optionLines.Count == 0)
        {
            return [];
        }

        if (optionLines.Count <= availableLineCount)
        {
            return optionLines;
        }

        int selectedLineIndex = GetSelectedOptionFirstLineIndex();
        int startIndex = Math.Clamp(
            selectedLineIndex - (availableLineCount / 2),
            0,
            Math.Max(0, optionLines.Count - availableLineCount));

        List<SelectionRenderLine> visibleLines = optionLines
            .Skip(startIndex)
            .Take(availableLineCount)
            .ToList();

        if (availableLineCount >= 3 &&
            startIndex > 0 &&
            visibleLines.Count > 0)
        {
            visibleLines[0] = new SelectionRenderLine("[grey]...[/]");
        }

        if (availableLineCount >= 3 &&
            startIndex + availableLineCount < optionLines.Count &&
            visibleLines.Count > 0)
        {
            visibleLines[^1] = new SelectionRenderLine("[grey]...[/]");
        }

        return visibleLines;
    }

    private IReadOnlyList<string> GetVisibleDescriptionLines(int contentWidth)
    {
        if (_descriptionSupportsMarkup)
        {
            IReadOnlyList<string> logicalLines = SplitDescriptionLines(Description);
            if (logicalLines.Count <= MaxDescriptionLineCount)
            {
                return logicalLines;
            }

            return
            [
                .. logicalLines.Take(MaxDescriptionLineCount - 1),
                "..."
            ];
        }

        IReadOnlyList<string> wrappedLines = WrapPlainText(Description, contentWidth);
        if (wrappedLines.Count <= MaxDescriptionLineCount)
        {
            return wrappedLines;
        }

        return [
            .. wrappedLines.Take(MaxDescriptionLineCount - 1),
            "..."
        ];
    }

    private IReadOnlyList<string> GetVisibleDescriptionMarkupLines(int contentWidth)
    {
        if (_descriptionSupportsMarkup)
        {
            IReadOnlyList<string> logicalLines = GetVisibleDescriptionLines(contentWidth);
            return logicalLines
                .Select(line => line == "..." ? "[grey]...[/]" : line)
                .ToArray();
        }

        return GetVisibleDescriptionLines(contentWidth)
            .Select(line => Markup.Escape(line))
            .ToArray();
    }

    private static IReadOnlyList<string> SplitDescriptionLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);
    }

    private int GetSelectedOptionFirstLineIndex()
    {
        int contentWidth = GetContentWidth();
        int lineIndex = 0;

        for (int index = 0; index < SelectedIndex && index < _options.Count; index++)
        {
            SelectionPromptOption<T> option = _options[index];
            if (ShouldRenderSectionHeader(index))
            {
                if (lineIndex > 0)
                {
                    lineIndex++;
                }

                lineIndex += CountWrappedLines(option.Section, contentWidth);
            }

            lineIndex += CountWrappedLines($"> {index + 1}. {option.Label}", contentWidth);

            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                lineIndex += CountWrappedLines($"    {option.Description}", contentWidth);
            }
        }

        return lineIndex;
    }

    private bool ShouldRenderSectionHeader(int optionIndex)
    {
        if (optionIndex < 0 ||
            optionIndex >= _options.Count ||
            string.IsNullOrWhiteSpace(_options[optionIndex].Section))
        {
            return false;
        }

        return optionIndex == 0 ||
            !string.Equals(
                _options[optionIndex - 1].Section,
                _options[optionIndex].Section,
                StringComparison.Ordinal);
    }

    private static int GetContentWidth()
    {
        return Math.Max(20, GetWindowWidth() - PanelHorizontalPadding);
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

    private static IReadOnlyList<string> WrapMarkupLines(
        string? text,
        int width,
        string prefixMarkup = "",
        string suffixMarkup = "")
    {
        return WrapPlainText(text, width)
            .Select(line => prefixMarkup + Markup.Escape(line) + suffixMarkup)
            .ToArray();
    }

    private static IReadOnlyList<string> WrapPlainText(
        string? text,
        int width)
    {
        return WrapPlainText(text, width, width);
    }

    private static IReadOnlyList<string> WrapPlainText(
        string? text,
        int firstLineWidth,
        int continuationLineWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        List<string> lines = [];
        int currentWidth = Math.Max(1, firstLineWidth);
        int safeContinuationWidth = Math.Max(1, continuationLineWidth);

        foreach (string logicalLine in text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None))
        {
            if (logicalLine.Length == 0)
            {
                lines.Add(string.Empty);
                currentWidth = safeContinuationWidth;
                continue;
            }

            int offset = 0;
            while (offset < logicalLine.Length)
            {
                int length = Math.Min(currentWidth, logicalLine.Length - offset);
                lines.Add(logicalLine.Substring(offset, length));
                offset += length;
                currentWidth = safeContinuationWidth;
            }
        }

        return lines;
    }

    private static int CountWrappedLines(string? text, int width)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int safeWidth = Math.Max(1, width);
        int lineCount = 0;

        foreach (string line in text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None))
        {
            lineCount += Math.Max(1, (int)Math.Ceiling(line.Length / (double)safeWidth));
        }

        return lineCount;
    }

    private sealed record SelectionRenderLine(string Markup, int? OptionIndex = null);
}
