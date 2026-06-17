namespace NanoAgent.CLI;

public static partial class Program
{
    // ---- Reader view ----

    private static void EnterReaderView(AppState state)
    {
        if (state.ActiveModal is not null)
        {
            return;
        }

        if (state.IsCopyModeActive)
        {
            ExitCopyMode(state);
        }

        state.IsReaderViewActive = true;
        state.ReaderScrollOffset = GetMaxReaderScrollOffset(state);
        state.ReaderViewDirty = true;
    }

    private static void ExitReaderView(AppState state)
    {
        state.IsReaderViewActive = false;
        state.ReaderViewDirty = true;
    }

    private static void ScrollReaderView(AppState state, int delta)
    {
        SetReaderScroll(state, state.ReaderScrollOffset + delta);
    }

    private static void SetReaderScroll(AppState state, int offset)
    {
        int maxScrollOffset = GetMaxReaderScrollOffset(state);
        state.ReaderScrollOffset = Math.Clamp(offset, 0, maxScrollOffset);
        state.ReaderViewDirty = true;
    }

    private static void HandleReaderViewKey(AppState state, ConsoleKeyInfo key)
    {
        int step = MouseWheelScrollLineCount;
        int page = Math.Max(1, GetReaderViewportLineCount() - 1);

        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.F5:
                ExitReaderView(state);
                return;

            case ConsoleKey.UpArrow:
                ScrollReaderView(state, -step);
                return;

            case ConsoleKey.DownArrow:
                ScrollReaderView(state, step);
                return;

            case ConsoleKey.PageUp:
                ScrollReaderView(state, -page);
                return;

            case ConsoleKey.PageDown:
                ScrollReaderView(state, page);
                return;

            case ConsoleKey.Home:
                SetReaderScroll(state, 0);
                return;

            case ConsoleKey.End:
                SetReaderScroll(state, int.MaxValue);
                return;
        }

        // All other keys are intentionally swallowed while the reader view is open.
    }

    private static void HandleReaderViewSequence(AppState state, string sequence)
    {
        int step = MouseWheelScrollLineCount;
        int page = Math.Max(1, GetReaderViewportLineCount() - 1);

        if (sequence == "15~")
        {
            ExitReaderView(state);
            return;
        }

        if (sequence.EndsWith('A'))
        {
            ScrollReaderView(state, -step);
            return;
        }

        if (sequence.EndsWith('B'))
        {
            ScrollReaderView(state, step);
            return;
        }

        switch (sequence)
        {
            case "5~":
                ScrollReaderView(state, -page);
                return;

            case "6~":
                ScrollReaderView(state, page);
                return;

            case "H":
            case "1~":
                SetReaderScroll(state, 0);
                return;

            case "F":
            case "4~":
                SetReaderScroll(state, int.MaxValue);
                return;
        }

        // Other sequences are swallowed.
    }

    // ---- Copy mode ----

    private static void EnterCopyMode(AppState state)
    {
        if (state.ActiveModal is not null)
        {
            return;
        }

        if (state.IsReaderViewActive)
        {
            ExitReaderView(state);
        }

        int lineCount = BuildConversationLines(state, GetMessageContentWidth()).Count;
        state.IsCopyModeActive = true;
        state.CopyAnchorLine = null;
        state.CopyCursorLine = Math.Max(0, lineCount - 1);
    }

    private static void ExitCopyMode(AppState state)
    {
        state.IsCopyModeActive = false;
        state.CopyAnchorLine = null;
    }

    private static void MoveCopyCursor(AppState state, int delta, bool extend)
    {
        int lineCount = BuildConversationLines(state, GetMessageContentWidth()).Count;
        if (lineCount == 0)
        {
            return;
        }

        if (extend && state.CopyAnchorLine is null)
        {
            state.CopyAnchorLine = state.CopyCursorLine;
        }

        state.CopyCursorLine = Math.Clamp(state.CopyCursorLine + delta, 0, lineCount - 1);
    }

    private static void MoveCopyCursorTo(AppState state, int target, bool extend)
    {
        int lineCount = BuildConversationLines(state, GetMessageContentWidth()).Count;
        if (lineCount == 0)
        {
            return;
        }

        if (extend && state.CopyAnchorLine is null)
        {
            state.CopyAnchorLine = state.CopyCursorLine;
        }

        state.CopyCursorLine = Math.Clamp(target, 0, lineCount - 1);
    }

    private static void ToggleCopyAnchor(AppState state)
    {
        state.CopyAnchorLine = state.CopyAnchorLine is null
            ? state.CopyCursorLine
            : null;
    }

    private static void SelectAllCopy(AppState state)
    {
        int lineCount = BuildConversationLines(state, GetMessageContentWidth()).Count;
        if (lineCount == 0)
        {
            return;
        }

        state.CopyAnchorLine = 0;
        state.CopyCursorLine = lineCount - 1;
    }

    private static void ExecuteCopySelection(AppState state)
    {
        List<ConversationLine> lines = BuildConversationLines(state, GetMessageContentWidth());
        string text = BuildCopySelectionText(state, lines);

        ExitCopyMode(state);

        if (string.IsNullOrEmpty(text))
        {
            state.AddSystemMessage("Nothing selected to copy.");
            return;
        }

        int copiedLineCount = text.Split('\n').Length;
        state.AddSystemMessage(TryWriteClipboardText(text)
            ? $"Copied {copiedLineCount} line{(copiedLineCount == 1 ? string.Empty : "s")} to the clipboard."
            : "Could not copy: no clipboard tool was found.");
    }

    private static void HandleCopyModeKey(AppState state, ConsoleKeyInfo key)
    {
        bool extend = key.Modifiers.HasFlag(ConsoleModifiers.Shift) || IsShiftKeyPressed();
        int page = Math.Max(1, GetMessageViewportLineCount(state) - 1);

        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.F6:
                ExitCopyMode(state);
                return;

            case ConsoleKey.Enter:
                ExecuteCopySelection(state);
                return;

            case ConsoleKey.UpArrow:
                MoveCopyCursor(state, -1, extend);
                return;

            case ConsoleKey.DownArrow:
                MoveCopyCursor(state, 1, extend);
                return;

            case ConsoleKey.PageUp:
                MoveCopyCursor(state, -page, extend);
                return;

            case ConsoleKey.PageDown:
                MoveCopyCursor(state, page, extend);
                return;

            case ConsoleKey.Home:
                MoveCopyCursorTo(state, 0, extend);
                return;

            case ConsoleKey.End:
                MoveCopyCursorTo(state, int.MaxValue, extend);
                return;
        }

        switch (key.KeyChar)
        {
            case 'v':
            case ' ':
                ToggleCopyAnchor(state);
                return;

            case 'y':
            case 'c':
                ExecuteCopySelection(state);
                return;

            case 'a':
                SelectAllCopy(state);
                return;
        }

        // Other keys are swallowed while copy mode is active.
    }

    private static void HandleCopyModeSequence(AppState state, string sequence)
    {
        bool extend = sequence.Contains(";2", StringComparison.Ordinal);
        int page = Math.Max(1, GetMessageViewportLineCount(state) - 1);

        if (sequence == "17~")
        {
            ExitCopyMode(state);
            return;
        }

        if (sequence.EndsWith('A'))
        {
            MoveCopyCursor(state, -1, extend);
            return;
        }

        if (sequence.EndsWith('B'))
        {
            MoveCopyCursor(state, 1, extend);
            return;
        }

        string baseSequence = sequence.EndsWith('~')
            ? sequence[..^1].Split(';')[0] + "~"
            : sequence;

        switch (baseSequence)
        {
            case "5~":
                MoveCopyCursor(state, -page, extend);
                return;

            case "6~":
                MoveCopyCursor(state, page, extend);
                return;

            case "H":
            case "1~":
                MoveCopyCursorTo(state, 0, extend);
                return;

            case "F":
            case "4~":
                MoveCopyCursorTo(state, int.MaxValue, extend);
                return;
        }

        // Other sequences are swallowed.
    }

    // Routes terminal CSI sequences while a selection mode is active, or enters a mode
    // via its function-key sequence. Returns true when the sequence was consumed.
    private static bool HandleSelectionTerminalSequence(AppState state, string sequence)
    {
        if (state.IsReaderViewActive)
        {
            HandleReaderViewSequence(state, sequence);
            return true;
        }

        if (state.IsCopyModeActive)
        {
            HandleCopyModeSequence(state, sequence);
            return true;
        }

        if (state.ActiveModal is not null)
        {
            return false;
        }

        switch (sequence)
        {
            case "15~":
                EnterReaderView(state);
                return true;

            case "17~":
                EnterCopyMode(state);
                return true;
        }

        return false;
    }
}
