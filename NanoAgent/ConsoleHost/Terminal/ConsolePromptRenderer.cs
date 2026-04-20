using NanoAgent.Application.Models;
using Spectre.Console;

namespace NanoAgent.ConsoleHost.Terminal;

internal sealed class ConsolePromptRenderer : IConsolePromptRenderer
{
    private readonly IConsoleTerminal _terminal;
    private readonly IAnsiConsole _console;

    public ConsolePromptRenderer(
        IConsoleTerminal terminal,
        IAnsiConsole console)
    {
        _terminal = terminal;
        _console = console;
    }

    public int WriteInteractiveSelectionPrompt<T>(SelectionPromptRequest<T> request, int selectedIndex)
    {
        WriteHeading(request.Title, request.Description);
        _terminal.WriteLine(BuildInteractiveInstructions(request.AllowCancellation));
        _terminal.WriteLine();

        int optionsTop = _terminal.CursorTop;
        WriteSelectionOptions(request.Options, selectedIndex);
        return optionsTop;
    }

    public void RewriteSelectionOptions<T>(
        SelectionPromptRequest<T> request,
        int selectedIndex,
        int optionsTop)
    {
        _terminal.SetCursorPosition(0, optionsTop);
        WriteSelectionOptions(request.Options, selectedIndex);
        _terminal.SetCursorPosition(0, optionsTop + request.Options.Count);
    }

    public void WriteFallbackSelectionPrompt<T>(SelectionPromptRequest<T> request)
    {
        WriteHeading(request.Title, request.Description);

        for (int index = 0; index < request.Options.Count; index++)
        {
            SelectionPromptOption<T> option = request.Options[index];
            _terminal.WriteLine($"{index + 1}. {BuildOptionLabel(option)}");
        }

        _terminal.WriteLine();
    }

    public void WriteSecretPrompt(SecretPromptRequest request)
    {
        WriteHeading(request.Label, request.Description);
        _terminal.Write("> ");
    }

    public void WriteStatus(StatusMessageKind kind, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        string prefix = kind switch
        {
            StatusMessageKind.Error => "[error]",
            StatusMessageKind.Success => "[ok]",
            _ => "[info]"
        };

        if (_terminal.IsOutputRedirected)
        {
            _console.WriteLine($"{prefix} {message}");
            return;
        }

        Style style = kind switch
        {
            StatusMessageKind.Error => new Style(Color.White, Color.Red),
            StatusMessageKind.Success => new Style(Color.Black, Color.Green),
            _ => new Style(Color.Black, Color.Aqua)
        };

        WriteStyledLine($"{prefix} {message}", style);
    }

    public void WriteTextPrompt(TextPromptRequest request)
    {
        WriteHeading(request.Label, request.Description);
        if (!string.IsNullOrWhiteSpace(request.DefaultValue))
        {
            _terminal.WriteLine($"Default: {request.DefaultValue}");
        }

        _terminal.Write("> ");
    }

    private string BuildOptionLabel<T>(SelectionPromptOption<T> option)
    {
        return string.IsNullOrWhiteSpace(option.Description)
            ? option.Label
            : $"{option.Label} - {option.Description}";
    }

    private string BuildInteractiveInstructions(bool allowCancellation)
    {
        return allowCancellation
            ? "Use Up/Down to move, Enter to confirm, Esc to cancel."
            : "Use Up/Down to move and Enter to confirm.";
    }

    private string FormatInteractiveOption<T>(SelectionPromptOption<T> option, bool isSelected)
    {
        string prefix = isSelected ? "> " : "  ";
        return prefix + BuildOptionLabel(option);
    }

    private int GetLineWidth()
    {
        return _terminal.WindowWidth > 0 ? _terminal.WindowWidth : 80;
    }

    private string PadLine(string value)
    {
        int width = Math.Max(1, GetLineWidth() - 1);
        string trimmed = value.Length > width
            ? value[..Math.Max(0, width - 3)] + "..."
            : value;

        return trimmed.PadRight(width);
    }

    private void WriteHeading(string title, string? description)
    {
        _terminal.WriteLine(title);
        if (!string.IsNullOrWhiteSpace(description))
        {
            _terminal.WriteLine(description);
        }
    }

    private void WriteSelectionOptions<T>(
        IReadOnlyList<SelectionPromptOption<T>> options,
        int selectedIndex)
    {
        for (int index = 0; index < options.Count; index++)
        {
            bool isSelected = index == selectedIndex;
            string line = PadLine(FormatInteractiveOption(options[index], isSelected));

            if (isSelected)
            {
                WriteStyledLine(line, new Style(Color.Black, Color.Grey));
            }
            else
            {
                _console.WriteLine(line);
            }
        }
    }

    private void WriteStyledLine(string text, Style style)
    {
        _console.WriteLine(text, style);
    }
}
