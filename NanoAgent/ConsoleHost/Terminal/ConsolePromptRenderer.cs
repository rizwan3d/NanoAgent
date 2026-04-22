using NanoAgent.Application.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NanoAgent.ConsoleHost.Terminal;

internal sealed class ConsolePromptRenderer : IConsolePromptRenderer
{
    private static readonly Style TitleStyle = new(Color.Aqua, decoration: Decoration.Bold);
    private static readonly Style DescriptionStyle = new(Color.Grey);
    private static readonly Style InstructionStyle = new(Color.Grey);
    private static readonly Style OptionStyle = new(Color.White);
    private static readonly Style SelectedOptionStyle = new(Color.Black, Color.Aqua, Decoration.Bold);
    private static readonly Style PromptStyle = new(Color.Aqua, decoration: Decoration.Bold);
    private static readonly Style DefaultStyle = new(Color.Yellow);

    private readonly IConsoleTerminal _terminal;
    private readonly IAnsiConsole _console;

    public ConsolePromptRenderer(
        IConsoleTerminal terminal,
        IAnsiConsole console)
    {
        _terminal = terminal;
        _console = console;
    }

    public InteractiveSelectionPromptLayout WriteInteractiveSelectionPrompt<T>(
        SelectionPromptRequest<T> request,
        int selectedIndex,
        int? remainingAutoSelectSeconds = null)
    {
        string instructions = BuildInteractiveInstructions(request.AllowCancellation);
        string? defaultLine = BuildDefaultLine(request, remainingAutoSelectSeconds);

        EnsurePromptStartsOnNewLine();
        WriteHeading(request.Title, request.Description);
        if (defaultLine is not null)
        {
            WriteStyledLine(defaultLine, InstructionStyle);
        }

        WriteStyledLine(instructions, InstructionStyle);
        _terminal.WriteLine();

        WriteSelectionOptions(request.Options, selectedIndex);

        int headingLineCount =
            CountLogicalLines(request.Title) +
            CountLogicalLines(request.Description);
        int defaultLineCount = defaultLine is null ? 0 : 1;
        int totalLineCount =
            headingLineCount +
            defaultLineCount +
            CountLogicalLines(instructions) +
            1 +
            request.Options.Count;
        int promptBottom = _terminal.CursorTop;
        int promptTop = Math.Max(0, promptBottom - totalLineCount);
        int optionsTop = Math.Max(0, promptBottom - request.Options.Count);
        int defaultLineTop = defaultLine is null
            ? -1
            : promptTop + headingLineCount;

        return new InteractiveSelectionPromptLayout(
            promptTop,
            optionsTop,
            totalLineCount,
            defaultLineTop);
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

    public void RewriteSelectionDefaultLine<T>(
        SelectionPromptRequest<T> request,
        InteractiveSelectionPromptLayout layout,
        int remainingAutoSelectSeconds)
    {
        if (layout.DefaultLineTop < 0 ||
            !TrySetCursorPosition(0, layout.DefaultLineTop))
        {
            return;
        }

        string? defaultLine = BuildDefaultLine(request, remainingAutoSelectSeconds);
        if (defaultLine is null)
        {
            return;
        }

        WriteStyledLine(PadLine(defaultLine), InstructionStyle);
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

            _console.Write(new Text(new string(' ', width)));
        }

        TrySetCursorPosition(0, layout.PromptTop);
    }

    public void WriteSecretPrompt(SecretPromptRequest request)
    {
        EnsurePromptStartsOnNewLine();
        WriteHeading(request.Label, request.Description);
        WriteStyledInline("> ", PromptStyle);
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
            WriteStyledInline("Default: ", InstructionStyle);
            WriteStyledLine(request.DefaultValue, DefaultStyle);
        }

        WriteStyledInline("> ", PromptStyle);
    }

    private static string BuildOptionLabel<T>(SelectionPromptOption<T> option)
    {
        return string.IsNullOrWhiteSpace(option.Description)
            ? option.Label
            : $"{option.Label} - {option.Description}";
    }

    private static string BuildInteractiveInstructions(bool allowCancellation)
    {
        return allowCancellation
            ? "Use Up/Down to move, Enter to confirm, Esc to cancel."
            : "Use Up/Down to move and Enter to confirm.";
    }

    private static string? BuildDefaultLine<T>(
        SelectionPromptRequest<T> request,
        int? remainingAutoSelectSeconds)
    {
        if (remainingAutoSelectSeconds is null)
        {
            return null;
        }

        SelectionPromptOption<T> defaultOption = request.Options[request.DefaultIndex];
        return $"Default ({remainingAutoSelectSeconds.Value}s): {defaultOption.Label}";
    }

    private static string FormatInteractiveOption<T>(SelectionPromptOption<T> option, bool isSelected)
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

        return SplitLogicalLines(value).Length;
    }

    private void WriteHeading(string title, string? description)
    {
        WriteStyledLogicalLines(title, TitleStyle);
        if (!string.IsNullOrWhiteSpace(description))
        {
            WriteStyledLogicalLines(description, DescriptionStyle);
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
                WriteStyledLine(line, SelectedOptionStyle);
            }
            else
            {
                WriteStyledLine(line, OptionStyle);
            }
        }
    }

    private void WriteStyledLine(string text, Style style)
    {
        _console.Write(new Text(text, style));
        _console.WriteLine();
    }

    private void WriteStyledInline(string text, Style style)
    {
        _console.Write(new Text(text, style));
    }

    private void WriteStyledLogicalLines(string text, Style style)
    {
        foreach (string line in SplitLogicalLines(text))
        {
            WriteStyledLine(line, style);
        }
    }

    private static string[] SplitLogicalLines(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);
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
