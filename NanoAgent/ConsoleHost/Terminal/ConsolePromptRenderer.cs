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

    public InteractiveSelectionPromptLayout WriteInteractiveSelectionPrompt<T>(SelectionPromptRequest<T> request, int selectedIndex)
    {
        EnsurePromptStartsOnNewLine();
        WriteHeading(request.Title, request.Description);
        _terminal.WriteLine(BuildInteractiveInstructions(request.AllowCancellation));
        _terminal.WriteLine();

        WriteSelectionOptions(request.Options, selectedIndex);

        int totalLineCount =
            CountLogicalLines(request.Title) +
            CountLogicalLines(request.Description) +
            CountLogicalLines(BuildInteractiveInstructions(request.AllowCancellation)) +
            1 +
            request.Options.Count;
        int promptBottom = _terminal.CursorTop;
        int promptTop = Math.Max(0, promptBottom - totalLineCount);
        int optionsTop = Math.Max(0, promptBottom - request.Options.Count);

        return new InteractiveSelectionPromptLayout(
            promptTop,
            optionsTop,
            totalLineCount);
    }

    public void RewriteSelectionOptions<T>(
        SelectionPromptRequest<T> request,
        int selectedIndex,
        InteractiveSelectionPromptLayout layout)
    {
        if (!TrySetCursorPosition(0, layout.OptionsTop))
        {
            return;
        }

        WriteSelectionOptions(request.Options, selectedIndex);
    }

    public void ClearInteractiveSelectionPrompt(InteractiveSelectionPromptLayout layout)
    {
        if (TryDeleteInteractiveSelectionPrompt(layout))
        {
            return;
        }

        int width = Math.Max(1, GetLineWidth() - 1);

        for (int offset = 0; offset < layout.TotalLineCount; offset++)
        {
            int top = layout.PromptTop + offset;
            if (!TrySetCursorPosition(0, top))
            {
                break;
            }

            _console.Write(new string(' ', width));
        }

        TrySetCursorPosition(0, layout.PromptTop);
    }

    public void WriteFallbackSelectionPrompt<T>(SelectionPromptRequest<T> request)
    {
        EnsurePromptStartsOnNewLine();
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
        EnsurePromptStartsOnNewLine();
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
        EnsurePromptStartsOnNewLine();
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

    private void EnsurePromptStartsOnNewLine()
    {
        if (_terminal.CursorLeft != 0)
        {
            _terminal.WriteLine();
        }
    }

    private static int CountLogicalLines(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None)
            .Length;
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
                WriteHighlightedInteractiveLine(line);
            }
            else
            {
                _terminal.WriteLine(line);
            }
        }
    }

    private void WriteStyledLine(string text, Style style)
    {
        _console.WriteLine(text, style);
    }

    private void WriteHighlightedInteractiveLine(string text)
    {
        ConsoleColor previousForeground = _terminal.ForegroundColor;
        ConsoleColor previousBackground = _terminal.BackgroundColor;

        try
        {
            _terminal.ForegroundColor = ConsoleColor.Black;
            _terminal.BackgroundColor = ConsoleColor.Gray;
            _terminal.WriteLine(text);
        }
        finally
        {
            _terminal.ForegroundColor = previousForeground;
            _terminal.BackgroundColor = previousBackground;
        }
    }

    private bool TrySetCursorPosition(int left, int top)
    {
        try
        {
            _terminal.SetCursorPosition(left, top);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private bool TryDeleteInteractiveSelectionPrompt(InteractiveSelectionPromptLayout layout)
    {
        if (_terminal.IsOutputRedirected || layout.TotalLineCount <= 0)
        {
            return false;
        }

        try
        {
            _terminal.SetCursorPosition(0, layout.PromptTop);
            _terminal.Write($"\u001b[{layout.TotalLineCount}M");
            _terminal.SetCursorPosition(0, layout.PromptTop);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
