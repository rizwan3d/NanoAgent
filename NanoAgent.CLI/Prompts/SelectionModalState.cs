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

    private readonly Action<Exception>? _onCancelled;
    private readonly Action<T> _onSelected;
    private readonly IReadOnlyList<SelectionPromptOption<T>> _options;

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
        _onSelected = onSelected;
        _onCancelled = onCancelled;
        SelectedIndex = Math.Clamp(request.DefaultIndex, 0, Math.Max(0, _options.Count - 1));
    }

    public int SelectedIndex { get; private set; }

    public override int PanelSize
    {
        get
        {
            int contentWidth = Math.Max(20, Console.WindowWidth - PanelHorizontalPadding);
            int bodyLineCount = CountWrappedLines(Title, contentWidth);

            if (!string.IsNullOrWhiteSpace(Description))
            {
                bodyLineCount += 1 + CountWrappedLines(Description, contentWidth);
            }

            if (DeadlineUtc is not null)
            {
                bodyLineCount += 2;
            }

            bodyLineCount++;

            for (int index = 0; index < _options.Count; index++)
            {
                SelectionPromptOption<T> option = _options[index];
                bodyLineCount += CountWrappedLines($"> {index + 1}. {option.Label}", contentWidth);

                if (!string.IsNullOrWhiteSpace(option.Description))
                {
                    bodyLineCount += CountWrappedLines($"    {option.Description}", contentWidth);
                }
            }

            int requestedSize = bodyLineCount + PanelChromeLineCount;
            int maxPanelSize = Math.Max(
                PanelChromeLineCount + 1,
                Console.WindowHeight - ReservedLayoutLineCount);
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
        List<string> headingLines = BuildHeadingLines(contentWidth);
        List<string> optionLines = BuildOptionLines(contentWidth);

        if (headingLines.Count >= maxBodyLines)
        {
            headingLines = headingLines
                .Take(Math.Max(0, maxBodyLines - 1))
                .ToList();
        }

        int availableOptionLines = maxBodyLines - headingLines.Count;
        IReadOnlyList<string> visibleOptionLines = GetVisibleOptionLines(
            optionLines,
            availableOptionLines);

        return string.Join('\n', headingLines.Concat(visibleOptionLines)).TrimEnd();
    }

    public override string BuildFooterMarkup()
    {
        return AllowCancellation
            ? "[grey]Up/Down: select[/]  [grey]|[/]  [grey]Enter: confirm[/]  [grey]|[/]  [grey]Esc: cancel[/]"
            : "[grey]Up/Down: select[/]  [grey]|[/]  [grey]Enter: confirm[/]";
    }

    public override string BuildInputMarkup()
    {
        if (DeadlineUtc is not null)
        {
            return $"[yellow]Selection prompt active.[/] Use [bold]Up/Down[/] and [bold]Enter[/]. Auto-select in [red]{GetRemainingSeconds()}s[/].";
        }

        return "[yellow]Selection prompt active.[/] Use [bold]Up/Down[/] and [bold]Enter[/].";
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
    }

    private List<string> BuildHeadingLines(int contentWidth)
    {
        List<string> lines = [];
        lines.AddRange(WrapMarkupLines(Title, contentWidth, "[bold yellow]", "[/]"));

        if (!string.IsNullOrWhiteSpace(Description))
        {
            lines.Add(string.Empty);
            lines.AddRange(WrapMarkupLines(Description, contentWidth));
        }

        if (DeadlineUtc is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"[grey]Auto-select in {GetRemainingSeconds()}s[/]");
        }

        lines.Add(string.Empty);
        return lines;
    }

    private List<string> BuildOptionLines(int contentWidth)
    {
        List<string> lines = [];

        for (int index = 0; index < _options.Count; index++)
        {
            SelectionPromptOption<T> option = _options[index];
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
                lines.Add(selected
                    ? $"[black on green]{line}[/]"
                    : $"[green]{line}[/]");
            }

            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                foreach (string descriptionLine in WrapPlainText(option.Description, Math.Max(1, contentWidth - 4)))
                {
                    lines.Add($"[grey]    {Markup.Escape(descriptionLine)}[/]");
                }
            }
        }

        return lines;
    }

    private IReadOnlyList<string> GetVisibleOptionLines(
        IReadOnlyList<string> optionLines,
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

        List<string> visibleLines = optionLines
            .Skip(startIndex)
            .Take(availableLineCount)
            .ToList();

        if (availableLineCount >= 3 &&
            startIndex > 0 &&
            visibleLines.Count > 0)
        {
            visibleLines[0] = "[grey]...[/]";
        }

        if (availableLineCount >= 3 &&
            startIndex + availableLineCount < optionLines.Count &&
            visibleLines.Count > 0)
        {
            visibleLines[^1] = "[grey]...[/]";
        }

        return visibleLines;
    }

    private int GetSelectedOptionFirstLineIndex()
    {
        int contentWidth = GetContentWidth();
        int lineIndex = 0;

        for (int index = 0; index < SelectedIndex && index < _options.Count; index++)
        {
            SelectionPromptOption<T> option = _options[index];
            lineIndex += CountWrappedLines($"> {index + 1}. {option.Label}", contentWidth);

            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                lineIndex += CountWrappedLines($"    {option.Description}", contentWidth);
            }
        }

        return lineIndex;
    }

    private static int GetContentWidth()
    {
        return Math.Max(20, Console.WindowWidth - PanelHorizontalPadding);
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
}
