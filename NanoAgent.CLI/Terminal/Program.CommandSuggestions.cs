using Spectre.Console;

namespace NanoAgent.CLI;

public static partial class Program
{
    private static bool TryGetSlashCommandSuggestions(
        AppState state,
        out IReadOnlyList<SlashCommandSuggestion> suggestions)
    {
        suggestions = [];

        if (state.ActiveModal is not null ||
            state.SlashCommandSuggestionsDismissed)
        {
            return false;
        }

        string input = state.Input.ToString();
        if (!IsSlashCommandSuggestionInput(input))
        {
            return false;
        }

        suggestions = SlashCommandSuggestions
            .Where(suggestion => suggestion.Command.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (suggestions.Count == 0)
        {
            state.SlashCommandSuggestionIndex = 0;
            return false;
        }

        state.SlashCommandSuggestionIndex = Math.Clamp(
            state.SlashCommandSuggestionIndex,
            0,
            suggestions.Count - 1);
        return true;
    }

    private static bool IsSlashCommandSuggestionInput(string input)
    {
        if (string.IsNullOrEmpty(input) ||
            !input.StartsWith("/", StringComparison.Ordinal) ||
            input.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return input.Length == 1 ||
            SlashCommandSuggestions.Any(
                suggestion => suggestion.Command.StartsWith(input, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryHandleSlashCommandSuggestionInput(
        AppState state,
        ConsoleKeyInfo key)
    {
        if (!TryGetSlashCommandSuggestions(state, out IReadOnlyList<SlashCommandSuggestion> suggestions))
        {
            return false;
        }

        if (IsEnterKey(key))
        {
            AcceptSlashCommandSuggestion(state, suggestions, submitCommand: true);
            return true;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.LeftArrow:
                MoveSlashCommandSuggestion(state, suggestions, -1);
                return true;

            case ConsoleKey.DownArrow:
            case ConsoleKey.RightArrow:
                if (key.Key == ConsoleKey.RightArrow)
                {
                    AcceptSlashCommandSuggestion(state, suggestions, submitCommand: false);
                }
                else
                {
                    MoveSlashCommandSuggestion(state, suggestions, 1);
                }

                return true;

            case ConsoleKey.PageUp:
                MoveSlashCommandSuggestion(state, suggestions, -MaxSlashCommandSuggestionCount);
                return true;

            case ConsoleKey.PageDown:
                MoveSlashCommandSuggestion(state, suggestions, MaxSlashCommandSuggestionCount);
                return true;

            case ConsoleKey.Home:
                state.SlashCommandSuggestionIndex = 0;
                return true;

            case ConsoleKey.End:
                state.SlashCommandSuggestionIndex = suggestions.Count - 1;
                return true;

            case ConsoleKey.Tab:
                AcceptSlashCommandSuggestion(state, suggestions, submitCommand: false);
                return true;

            default:
                return false;
        }
    }

    private static bool TryDismissSlashCommandSuggestions(AppState state)
    {
        if (!TryGetSlashCommandSuggestions(state, out _))
        {
            return false;
        }

        state.SlashCommandSuggestionsDismissed = true;
        return true;
    }

    private static bool TryHandleSlashCommandSuggestionSequence(
        AppState state,
        string sequence)
    {
        if (!TryGetSlashCommandSuggestions(state, out IReadOnlyList<SlashCommandSuggestion> suggestions))
        {
            return false;
        }

        switch (sequence)
        {
            case "A":
                MoveSlashCommandSuggestion(state, suggestions, -1);
                return true;

            case "B":
                MoveSlashCommandSuggestion(state, suggestions, 1);
                return true;

            case "C":
                AcceptSlashCommandSuggestion(state, suggestions, submitCommand: false);
                return true;

            case "5~":
                MoveSlashCommandSuggestion(state, suggestions, -MaxSlashCommandSuggestionCount);
                return true;

            case "6~":
                MoveSlashCommandSuggestion(state, suggestions, MaxSlashCommandSuggestionCount);
                return true;

            case "H":
            case "1~":
                state.SlashCommandSuggestionIndex = 0;
                return true;

            case "F":
            case "4~":
                state.SlashCommandSuggestionIndex = suggestions.Count - 1;
                return true;

            default:
                return false;
        }
    }

    private static void MoveSlashCommandSuggestion(
        AppState state,
        IReadOnlyList<SlashCommandSuggestion> suggestions,
        int delta)
    {
        if (suggestions.Count == 0)
        {
            state.SlashCommandSuggestionIndex = 0;
            return;
        }

        int nextIndex = state.SlashCommandSuggestionIndex + delta;
        while (nextIndex < 0)
        {
            nextIndex += suggestions.Count;
        }

        state.SlashCommandSuggestionIndex = nextIndex % suggestions.Count;
    }

    private static void AcceptSlashCommandSuggestion(
        AppState state,
        IReadOnlyList<SlashCommandSuggestion> suggestions,
        bool submitCommand)
    {
        if (suggestions.Count == 0)
        {
            return;
        }

        SlashCommandSuggestion suggestion = suggestions[state.SlashCommandSuggestionIndex];
        state.Input.Clear();
        state.Input.Append(suggestion.RequiresArgument
            ? suggestion.Command + " "
            : suggestion.Command);
        state.SlashCommandSuggestionsDismissed = true;

        if (submitCommand && !suggestion.RequiresArgument)
        {
            SubmitInput(state);
        }
    }

    private static void ResetSlashCommandSuggestions(AppState state)
    {
        state.SlashCommandSuggestionIndex = 0;
        state.SlashCommandSuggestionsDismissed = false;
    }

    private static IReadOnlyList<SlashCommandSuggestion> GetVisibleSlashCommandSuggestions(
        AppState state,
        IReadOnlyList<SlashCommandSuggestion> suggestions)
    {
        if (suggestions.Count <= MaxSlashCommandSuggestionCount)
        {
            return suggestions;
        }

        int startIndex = Math.Clamp(
            state.SlashCommandSuggestionIndex - (MaxSlashCommandSuggestionCount / 2),
            0,
            suggestions.Count - MaxSlashCommandSuggestionCount);

        return suggestions
            .Skip(startIndex)
            .Take(MaxSlashCommandSuggestionCount)
            .ToArray();
    }

    private static string BuildSlashCommandSuggestionsMarkup(
        AppState state,
        IReadOnlyList<SlashCommandSuggestion> suggestions)
    {
        int contentWidth = Math.Max(20, Console.WindowWidth - 10);
        IReadOnlyList<SlashCommandSuggestion> visibleSuggestions = GetVisibleSlashCommandSuggestions(
            state,
            suggestions);
        int firstVisibleIndex = GetSlashCommandSuggestionIndex(
            suggestions,
            visibleSuggestions[0]);
        List<string> lines =
        [
            $"[grey]Commands matching [/][green]{Markup.Escape(state.Input.ToString())}[/][grey]:[/]"
        ];

        for (int visibleIndex = 0; visibleIndex < visibleSuggestions.Count; visibleIndex++)
        {
            int suggestionIndex = firstVisibleIndex + visibleIndex;
            SlashCommandSuggestion suggestion = visibleSuggestions[visibleIndex];
            bool selected = suggestionIndex == state.SlashCommandSuggestionIndex;
            string prefix = selected ? "> " : "  ";
            string usageText = TruncateFromRight(prefix + suggestion.Usage, Math.Min(34, contentWidth));
            int descriptionWidth = Math.Max(0, contentWidth - usageText.Length - 3);
            string description = descriptionWidth == 0
                ? string.Empty
                : TruncateFromRight(suggestion.Description, descriptionWidth);
            string plainLine = description.Length == 0
                ? usageText
                : $"{usageText} - {description}";

            lines.Add(selected
                ? $"[black on green]{Markup.Escape(plainLine)}[/]"
                : $"[green]{Markup.Escape(usageText)}[/][grey]{Markup.Escape(description.Length == 0 ? string.Empty : " - " + description)}[/]");
        }

        if (suggestions.Count > MaxSlashCommandSuggestionCount)
        {
            lines.Add($"[grey]{suggestions.Count} matches. Keep typing to narrow.[/]");
        }

        return string.Join('\n', lines);
    }

    private static int GetSlashCommandSuggestionLineCount(
        IReadOnlyList<SlashCommandSuggestion> suggestions)
    {
        if (suggestions.Count == 0)
        {
            return 0;
        }

        int count = 1 + Math.Min(suggestions.Count, MaxSlashCommandSuggestionCount);
        return suggestions.Count > MaxSlashCommandSuggestionCount
            ? count + 1
            : count;
    }

    private static int GetSlashCommandSuggestionIndex(
        IReadOnlyList<SlashCommandSuggestion> suggestions,
        SlashCommandSuggestion suggestion)
    {
        for (int index = 0; index < suggestions.Count; index++)
        {
            if (string.Equals(suggestions[index].Command, suggestion.Command, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    private static string TruncateFromRight(string value, int maxLength)
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
            ? value[..maxLength]
            : value[..(maxLength - 3)] + "...";
    }
}
