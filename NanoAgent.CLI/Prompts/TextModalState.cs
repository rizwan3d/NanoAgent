using System.Text;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using Spectre.Console;

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
            Value.Append(defaultValue);
        }
    }

    public bool IsSecret { get; }

    public override int PanelSize => 10;

    public StringBuilder Value { get; } = new();

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

        return $"[bold green]>[/] {Markup.Escape(text)}{Program.BuildInputCursorMarkup()}";
    }

    public override void HandleKey(AppState state, ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace ||
            key.KeyChar is '\b' or '\u007f')
        {
            if (Value.Length > 0)
            {
                Value.Remove(Value.Length - 1, 1);
            }

            return;
        }

        if (key.Key == ConsoleKey.Enter ||
            key.KeyChar is '\r' or '\n')
        {
            Resolve(state);
            return;
        }

        if (key.Key == ConsoleKey.Escape && AllowCancellation)
        {
            Cancel(state);
            return;
        }

        if (!char.IsControl(key.KeyChar))
        {
            Value.Append(key.KeyChar);
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
}
