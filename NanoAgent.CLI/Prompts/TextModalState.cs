using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using Spectre.Console;
using System.Text;

namespace NanoAgent.CLI;

public sealed class TextModalState : UiModalState
{
    private readonly Action<Exception>? _onCancelled;
    private readonly Action<string> _onSubmitted;

    private TextModalState(
        string label,
        string? description,
        string? defaultValue,
        bool allowCancellation,
        bool isSecret,
        object completionToken,
        Action<string> onSubmitted,
        Action<Exception>? onCancelled)
        : base(label, description, allowCancellation, autoSelectAfter: null, completionToken)
    {
        IsSecret = isSecret;
        _onSubmitted = onSubmitted;
        _onCancelled = onCancelled;

        if (!string.IsNullOrEmpty(defaultValue))
        {
            AppendText(defaultValue);
        }
    }

    public bool IsSecret { get; }

    public override int PanelSize => 10;

    public StringBuilder Value { get; } = new();

    public int CursorIndex { get; private set; }

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        int cursorIndex = ClampCursor();
        Value.Insert(cursorIndex, normalized);
        CursorIndex = cursorIndex + normalized.Length;
    }

    public static TextModalState Create(
        TextPromptRequest request,
        bool isSecret,
        object completionToken,
        Action<string> onSubmitted,
        Action<Exception>? onCancelled = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(completionToken);
        ArgumentNullException.ThrowIfNull(onSubmitted);

        return new TextModalState(
            request.Label,
            request.Description,
            request.DefaultValue,
            request.AllowCancellation,
            isSecret,
            completionToken,
            onSubmitted,
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

        builder.AppendLine();
        builder.AppendLine("[grey]Type your value below and press Enter.[/]");
        return builder.ToString().TrimEnd();
    }

    public override string BuildFooterMarkup()
    {
        return AllowCancellation
            ? "[grey]Type to edit[/]  [grey]|[/]  [grey]Enter: submit[/]  [grey]|[/]  [grey]Esc: cancel[/]"
            : "[grey]Type to edit[/]  [grey]|[/]  [grey]Enter: submit[/]";
    }

    public override string BuildInputMarkup()
    {
        string text = IsSecret
            ? new string('*', Value.Length)
            : Value.ToString();

        if (string.IsNullOrEmpty(text))
        {
            text = IsSecret ? "(secret hidden)" : string.Empty;
        }

        int cursorIndex = Value.Length == 0 && IsSecret
            ? text.Length
            : Math.Clamp(CursorIndex, 0, text.Length);
        string beforeCursor = text[..cursorIndex];
        string afterCursor = text[cursorIndex..];
        return $"[bold green]>[/] {Markup.Escape(beforeCursor)}{Program.BuildInputCursorMarkup()}{Markup.Escape(afterCursor)}";
    }

    public override void HandleKey(AppState state, ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace ||
            key.KeyChar is '\b' or '\u007f')
        {
            int cursorIndex = ClampCursor();
            if (cursorIndex > 0)
            {
                Value.Remove(cursorIndex - 1, 1);
                CursorIndex = cursorIndex - 1;
            }

            return;
        }

        if (key.Key == ConsoleKey.Delete)
        {
            int cursorIndex = ClampCursor();
            if (cursorIndex < Value.Length)
            {
                Value.Remove(cursorIndex, 1);
            }

            return;
        }

        if (key.Key == ConsoleKey.LeftArrow)
        {
            CursorIndex = Math.Max(0, ClampCursor() - 1);
            return;
        }

        if (key.Key == ConsoleKey.RightArrow)
        {
            CursorIndex = Math.Min(Value.Length, ClampCursor() + 1);
            return;
        }

        if (key.Key == ConsoleKey.Home)
        {
            CursorIndex = 0;
            return;
        }

        if (key.Key == ConsoleKey.End)
        {
            CursorIndex = Value.Length;
            return;
        }

        if (key.Key == ConsoleKey.Enter ||
            key.KeyChar is '\r' or '\n')
        {
            Resolve(state);
            return;
        }

        if (IsCancellationKey(key) && AllowCancellation)
        {
            Cancel(state);
            return;
        }

        if (!char.IsControl(key.KeyChar))
        {
            int cursorIndex = ClampCursor();
            Value.Insert(cursorIndex, key.KeyChar);
            CursorIndex = cursorIndex + 1;
        }
    }

    protected override void ResolveByTimeout(AppState state)
    {
        Resolve(state);
    }

    private void Cancel(AppState state)
    {
        state.ActiveModal = null;
        _onCancelled?.Invoke(new PromptCancelledException());
    }

    private void Resolve(AppState state)
    {
        state.ActiveModal = null;
        _onSubmitted(Value.ToString());
    }

    private int ClampCursor()
    {
        CursorIndex = Math.Clamp(CursorIndex, 0, Value.Length);
        return CursorIndex;
    }
}
