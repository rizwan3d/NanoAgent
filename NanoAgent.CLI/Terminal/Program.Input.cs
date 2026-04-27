using System.Text;

namespace NanoAgent.CLI;

public static partial class Program
{
    private static void HandleInput(AppState state)
    {
        bool appendedInputInBatch = false;
        int pastedLineBreaksInBatch = 0;

        while (Console.KeyAvailable)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (IsEscapeKey(key) &&
                TryHandleTerminalEscapeInput(state))
            {
                continue;
            }

            if (state.ActiveModal is not null)
            {
                state.ActiveModal.HandleKey(state, key);
                return;
            }

            if (TrySkipLineFeedAfterCarriageReturn(state, key))
            {
                continue;
            }

            if (key.Key == ConsoleKey.C &&
                key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                state.Running = false;
                return;
            }

            if (IsEscapeKey(key))
            {
                if (TryDismissSlashCommandSuggestions(state))
                {
                    continue;
                }

                state.Running = false;
                return;
            }

            if (key.Key == ConsoleKey.F2)
            {
                RequestModelSelection(state);
                return;
            }

            if (TryHandleSlashCommandSuggestionInput(state, key))
            {
                return;
            }

            if (HandleConversationScrollInput(state, key))
            {
                continue;
            }

            if (IsBackspaceKey(key))
            {
                if (state.Input.Length > 0)
                {
                    state.Input.Remove(state.Input.Length - 1, 1);
                    ResetSlashCommandSuggestions(state);
                }

                continue;
            }

            if (IsEnterKey(key))
            {
                if (IsMultilineEnterKey(key))
                {
                    AppendInputLineBreak(state, key);
                    appendedInputInBatch = true;
                    continue;
                }

                if (IsLikelyPastedLineBreak(key, appendedInputInBatch, pastedLineBreaksInBatch))
                {
                    AppendInputLineBreak(state, key);
                    appendedInputInBatch = true;
                    pastedLineBreaksInBatch++;
                    continue;
                }

                SubmitInput(state);
                return;
            }

            if (!char.IsControl(key.KeyChar))
            {
                state.Input.Append(key.KeyChar);
                state.SkipNextInputLineFeed = false;
                ResetSlashCommandSuggestions(state);
                appendedInputInBatch = true;
            }
        }
    }

    private static bool TrySkipLineFeedAfterCarriageReturn(AppState state, ConsoleKeyInfo key)
    {
        if (!state.SkipNextInputLineFeed)
        {
            return false;
        }

        state.SkipNextInputLineFeed = false;
        return key.KeyChar == '\n';
    }

    private static bool IsBackspaceKey(ConsoleKeyInfo key)
    {
        return key.Key == ConsoleKey.Backspace ||
            key.KeyChar is '\b' or '\u007f';
    }

    private static bool IsEnterKey(ConsoleKeyInfo key)
    {
        return key.Key == ConsoleKey.Enter ||
            key.KeyChar is '\r' or '\n';
    }

    private static bool IsMultilineEnterKey(ConsoleKeyInfo key)
    {
        return IsEnterKey(key) &&
            (key.Modifiers.HasFlag(ConsoleModifiers.Shift) ||
                key.Modifiers.HasFlag(ConsoleModifiers.Control) ||
                IsShiftKeyPressed() ||
                IsControlKeyPressed());
    }

    private static bool IsLikelyPastedLineBreak(
        ConsoleKeyInfo key,
        bool appendedInputInBatch,
        int pastedLineBreaksInBatch)
    {
        return IsEnterKey(key) &&
            (HasBufferedInputAfterDelay(PasteContinuationReadTimeoutMilliseconds) ||
                pastedLineBreaksInBatch > 0 ||
                (appendedInputInBatch && key.KeyChar == '\n'));
    }

    private static bool IsEscapeKey(ConsoleKeyInfo key)
    {
        return key.Key == ConsoleKey.Escape ||
            key.KeyChar == '\u001b';
    }

    private static bool HandleConversationScrollInput(AppState state, ConsoleKeyInfo key)
    {
        int viewportLineCount = GetMessageViewportLineCount(state);
        int pageSize = Math.Max(1, viewportLineCount - 1);

        switch (key.Key)
        {
            case ConsoleKey.PageUp:
                ScrollConversation(state, pageSize);
                return true;

            case ConsoleKey.PageDown:
                ScrollConversation(state, -pageSize);
                return true;

            case ConsoleKey.UpArrow:
                ScrollConversation(state, MouseWheelScrollLineCount);
                return true;

            case ConsoleKey.DownArrow:
                ScrollConversation(state, -MouseWheelScrollLineCount);
                return true;

            case ConsoleKey.Home:
                state.ConversationScrollOffset = GetMaxConversationScrollOffset(state);
                return true;

            case ConsoleKey.End:
                state.ConversationScrollOffset = 0;
                return true;

            default:
                return false;
        }
    }

    private static bool TryHandleTerminalEscapeInput(AppState state)
    {
        if (!TryReadBufferedKey(out ConsoleKeyInfo prefixKey))
        {
            return false;
        }

        if (prefixKey.KeyChar == '[')
        {
            return ConsumeCsiInput(state);
        }

        if (prefixKey.KeyChar == 'O')
        {
            ConsumeSs3Input(state);
            return true;
        }

        return true;
    }

    private static bool ConsumeCsiInput(AppState state)
    {
        if (!TryReadBufferedKey(out ConsoleKeyInfo modeKey))
        {
            return true;
        }

        if (modeKey.KeyChar == '<')
        {
            ConsumeSgrMouseInput(state);
            return true;
        }

        if (modeKey.KeyChar == 'M')
        {
            ConsumeX10MouseInput(state);
            return true;
        }

        StringBuilder sequence = new();
        sequence.Append(modeKey.KeyChar);

        if (!IsAnsiFinalByte(modeKey.KeyChar))
        {
            while (TryReadBufferedKey(out ConsoleKeyInfo sequenceKey))
            {
                sequence.Append(sequenceKey.KeyChar);
                if (IsAnsiFinalByte(sequenceKey.KeyChar))
                {
                    break;
                }
            }
        }

        HandleCsiKeySequence(state, sequence.ToString());
        return true;
    }

    private static void ConsumeSgrMouseInput(AppState state)
    {
        StringBuilder sequence = new();

        while (TryReadBufferedKey(out ConsoleKeyInfo key))
        {
            char character = key.KeyChar;
            if (!char.IsDigit(character) &&
                character is not (';' or 'M' or 'm'))
            {
                return;
            }

            sequence.Append(character);

            if (character is 'M' or 'm')
            {
                HandleSgrMouseSequence(state, sequence.ToString());
                return;
            }
        }
    }

    private static void HandleSgrMouseSequence(AppState state, string sequence)
    {
        if (sequence.Length == 0 ||
            sequence[^1] is not ('M' or 'm'))
        {
            return;
        }

        string[] parts = sequence[..^1].Split(';');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out int buttonCode))
        {
            return;
        }

        HandleMouseButtonCode(state, buttonCode);
    }

    private static void ConsumeX10MouseInput(AppState state)
    {
        if (!TryReadBufferedKey(out ConsoleKeyInfo buttonKey))
        {
            return;
        }

        TryReadBufferedKey(out _);
        TryReadBufferedKey(out _);

        HandleMouseButtonCode(state, buttonKey.KeyChar - 32);
    }

    private static void ConsumeSs3Input(AppState state)
    {
        if (!TryReadBufferedKey(out ConsoleKeyInfo key))
        {
            return;
        }

        HandleTerminalKeySequence(state, key.KeyChar.ToString());
    }

    private static void HandleCsiKeySequence(AppState state, string sequence)
    {
        if (sequence == "200~")
        {
            ConsumeBracketedPasteInput(state);
            return;
        }

        if (TryDispatchModalTerminalKeySequence(state, sequence))
        {
            return;
        }

        HandleTerminalKeySequence(state, sequence);
    }

    private static void HandleTerminalKeySequence(AppState state, string sequence)
    {
        if (IsMultilineEnterTerminalSequence(sequence))
        {
            AppendInputLineBreak(state);
            return;
        }

        if (TryHandleSlashCommandSuggestionSequence(state, sequence))
        {
            return;
        }

        switch (sequence)
        {
            case "A":
                ScrollConversation(state, MouseWheelScrollLineCount);
                return;

            case "B":
                ScrollConversation(state, -MouseWheelScrollLineCount);
                return;

            case "Q":
            case "12~":
                RequestModelSelection(state);
                return;

            case "5~":
                ScrollConversation(state, Math.Max(1, GetMessageViewportLineCount(state) - 1));
                return;

            case "6~":
                ScrollConversation(state, -Math.Max(1, GetMessageViewportLineCount(state) - 1));
                return;

            case "H":
            case "1~":
                state.ConversationScrollOffset = GetMaxConversationScrollOffset(state);
                return;

            case "F":
            case "4~":
                state.ConversationScrollOffset = 0;
                return;
        }
    }

    private static bool IsMultilineEnterTerminalSequence(string sequence)
    {
        return sequence is "13;2u" or "13;4u" or "13;5u" or "13;6u" or "13;7u" or "13;8u";
    }

    private static void AppendInputLineBreak(AppState state)
    {
        AppendInputLineBreak(state, default);
    }

    private static void AppendInputLineBreak(AppState state, ConsoleKeyInfo key)
    {
        if (state.ActiveModal is null)
        {
            state.Input.Append('\n');
            ResetSlashCommandSuggestions(state);
        }

        state.SkipNextInputLineFeed = key.KeyChar == '\r';
    }

    private static void AppendInputText(AppState state, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (state.ActiveModal is TextModalState textModal)
        {
            textModal.AppendText(text);
            state.SkipNextInputLineFeed = false;
            return;
        }

        if (state.ActiveModal is not null)
        {
            return;
        }

        string normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        state.Input.Append(normalized);
        state.SkipNextInputLineFeed = false;
        ResetSlashCommandSuggestions(state);
    }

    private static void ConsumeBracketedPasteInput(AppState state)
    {
        StringBuilder pastedText = new();

        while (TryReadBufferedKey(out ConsoleKeyInfo key))
        {
            if (key.KeyChar == '\u001b' &&
                TryConsumeBracketedPasteTerminator(pastedText))
            {
                AppendInputText(state, pastedText.ToString());
                return;
            }

            pastedText.Append(key.KeyChar);
        }

        AppendInputText(state, pastedText.ToString());
    }

    private static bool TryConsumeBracketedPasteTerminator(StringBuilder pastedText)
    {
        if (!TryReadBufferedKey(out ConsoleKeyInfo prefixKey))
        {
            pastedText.Append('\u001b');
            return false;
        }

        if (prefixKey.KeyChar != '[')
        {
            pastedText.Append('\u001b');
            pastedText.Append(prefixKey.KeyChar);
            return false;
        }

        StringBuilder sequence = new();
        while (TryReadBufferedKey(out ConsoleKeyInfo sequenceKey))
        {
            sequence.Append(sequenceKey.KeyChar);
            if (IsAnsiFinalByte(sequenceKey.KeyChar))
            {
                break;
            }
        }

        if (sequence.ToString() == "201~")
        {
            return true;
        }

        pastedText.Append('\u001b');
        pastedText.Append('[');
        pastedText.Append(sequence);
        return false;
    }

    private static bool TryDispatchModalTerminalKeySequence(AppState state, string sequence)
    {
        if (state.ActiveModal is null)
        {
            return false;
        }

        ConsoleKey? key = sequence switch
        {
            "A" => ConsoleKey.UpArrow,
            "B" => ConsoleKey.DownArrow,
            "C" => ConsoleKey.RightArrow,
            "D" => ConsoleKey.LeftArrow,
            "H" or "1~" => ConsoleKey.Home,
            "F" or "4~" => ConsoleKey.End,
            "5~" => ConsoleKey.PageUp,
            "6~" => ConsoleKey.PageDown,
            _ => null
        };

        if (key is null)
        {
            return false;
        }

        state.ActiveModal.HandleKey(
            state,
            new ConsoleKeyInfo('\0', key.Value, false, false, false));
        return true;
    }

    private static bool TryReadBufferedKey(out ConsoleKeyInfo key)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(TerminalSequenceReadTimeoutMilliseconds);

        while (!Console.KeyAvailable)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                key = default;
                return false;
            }

            Thread.Sleep(1);
        }

        key = Console.ReadKey(intercept: true);
        return true;
    }

    private static bool HasBufferedInputAfterDelay(int timeoutMilliseconds)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMilliseconds);

        while (!Console.KeyAvailable)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(1);
        }

        return true;
    }

    private static bool IsAnsiFinalByte(char character)
    {
        return character is >= '@' and <= '~';
    }

    private static void HandleMouseButtonCode(AppState state, int buttonCode)
    {
        int normalizedButtonCode = buttonCode & ~0b1_1100;

        if (normalizedButtonCode == 64)
        {
            ScrollConversation(state, MouseWheelScrollLineCount);
        }
        else if (normalizedButtonCode == 65)
        {
            ScrollConversation(state, -MouseWheelScrollLineCount);
        }
    }
}
