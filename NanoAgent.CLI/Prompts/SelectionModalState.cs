using System.Text;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using Spectre.Console;

namespace NanoAgent.CLI;

public sealed class SelectionModalState<T> : UiModalState
{
    private const int MinimumPanelSize = 7;
    private const int PanelChromeLineCount = 2;
    private const int PanelHorizontalPadding = 6;
    private const int ReservedLayoutLineCount = 18;

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
        StringBuilder builder = new();
        builder.AppendLine($"[bold yellow]{Markup.Escape(Title)}[/]");

        if (!string.IsNullOrWhiteSpace(Description))
        {
            builder.AppendLine();
            builder.AppendLine(Markup.Escape(Description));
        }

        if (DeadlineUtc is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"[grey]Auto-select in {GetRemainingSeconds()}s[/]");
        }

        builder.AppendLine();

        for (int index = 0; index < _options.Count; index++)
        {
            SelectionPromptOption<T> option = _options[index];
            string label = $"{index + 1}. {option.Label}";
            string escapedLabel = Markup.Escape(label);

            builder.AppendLine(index == SelectedIndex
                ? $"[black on green]> {escapedLabel}[/]"
                : $"[green]  {escapedLabel}[/]");

            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                builder.AppendLine($"[grey]    {Markup.Escape(option.Description)}[/]");
            }
        }

        return builder.ToString().TrimEnd();
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

        if (key.Key == ConsoleKey.Escape && AllowCancellation)
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
