using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using System.Text;

namespace NanoAgent.CLI;

public sealed class AppState
{
    private int _nextMessageId = 1;
    private long _nextOperationId;

    public AppState(UiBridge uiBridge, INanoAgentBackend backend)
    {
        UiBridge = uiBridge;
        Backend = backend;
        PlanScrollOffset = 0;
    }

    public string? ActiveModelId { get; set; }

    public int? ActiveModelContextWindowTokens { get; set; }

    public Task? ActiveOperation { get; set; }

    public UiModalState? ActiveModal { get; set; }

    public string ActivityText { get; set; } = "Initializing NanoAgent backend";

    public INanoAgentBackend Backend { get; }

    public bool ClearBusyWhenStreamCompletes { get; set; }

    public bool HasFatalError { get; set; }

    public bool HasMadeFirstLlmCall { get; set; }

    public string? FatalExitMessage { get; set; }

    public StringBuilder Input { get; } = new();

    public List<CollapsedInputPaste> CollapsedInputPastes { get; } = [];

    public List<ConversationAttachment> InputAttachments { get; } = [];

    public int InputCursorIndex { get; set; }

    public int? InputSelectionAnchor { get; set; }

    public bool SkipNextInputLineFeed { get; set; }

    public bool SlashCommandSuggestionsDismissed { get; set; }

    public int SlashCommandSuggestionIndex { get; set; }

    public bool IsBusy { get; set; }

    public bool IsReady { get; set; }

    public bool IsStreaming { get; set; }

    public bool IsPlanPinned { get; set; }

    public int PlanScrollOffset { get; set; }

    public ExecutionPlanProgress? LatestPlanProgress { get; set; }

    public string? LatestPlanText { get; set; }

    public bool HasCompletedPlan =>
        LatestPlanProgress is { } progress &&
        progress.Tasks.Count > 0 &&
        progress.CompletedTaskCount >= progress.Tasks.Count;

    public CancellationTokenSource LifetimeCancellation { get; } = new();

    public CancellationTokenSource? TurnCancellation { get; set; }

    public void CancelTurn()
    {
        TurnCancellation?.Cancel();
    }

    public void ResetTurnCancellation()
    {
        TurnCancellation?.Dispose();
        TurnCancellation = null;
    }

    public void ClearPlanState()
    {
        IsPlanPinned = false;
        LatestPlanProgress = null;
        LatestPlanText = null;
    }

    public List<ChatMessage> Messages { get; } = [];

    public int ConversationScrollOffset { get; set; }

    public bool IsConversationAutoScrollPaused { get; set; }

    public int LastConversationLineCount { get; set; }

    // Thinking/reasoning messages render collapsed (a single summary line) by default.
    // A message id present in this set is expanded inline. Ctrl+T toggles all blocks at
    // once; clicking a block toggles just that one.
    public HashSet<int> ExpandedThinkingMessageIds { get; } = [];

    // Screen geometry captured on the last standard-layout render so mouse clicks can be
    // mapped back to conversation lines. TopRow is the 1-based terminal row of the first
    // visible conversation line (-1 when the messages view is not on screen); the id array
    // maps each visible viewport row to the thinking message it belongs to (null if none).
    public int MessagesContentTopRow { get; set; } = -1;

    public int?[] VisibleThinkingMessageIds { get; set; } = [];

    // Reader view: a full-screen, chrome-free plain-text transcript that pauses the
    // live redraw so the terminal's own mouse selection can grab clean text.
    public bool IsReaderViewActive { get; set; }

    public int ReaderScrollOffset { get; set; }

    public string? ReaderViewTitle { get; set; }

    public string? ReaderViewInstructions { get; set; }

    public IReadOnlyList<string>? ReaderViewLines { get; set; }

    // Set whenever the reader view must be repainted (on enter / scroll). While it is
    // false and the reader view is active, the render loop leaves the screen untouched
    // so a native selection is not wiped by the next frame.
    public bool ReaderViewDirty { get; set; }

    // Copy mode: an in-app, keyboard-driven line selection over the conversation that
    // copies the clean underlying text to the system clipboard.
    public bool IsCopyModeActive { get; set; }

    // Cursor position as an index into the full conversation line list.
    public int CopyCursorLine { get; set; }

    // Selection anchor; null until the user starts a selection.
    public int? CopyAnchorLine { get; set; }

    public DateTimeOffset? CurrentTurnStartedAt { get; set; }

    public string? PendingCompletionNote { get; set; }

    public bool IsTurnInterruptPending { get; set; }

    public Queue<PendingSubmission> PendingSubmissions { get; } = new();

    public Queue<ConsoleKeyInfo> PendingInputKeys { get; } = new();

    public string? ProviderName { get; set; }

    public string? ReasoningEffort { get; set; }

    public string RootDirectory { get; } = Directory.GetCurrentDirectory();

    // Git sidebar (F7): a VS Code-style left panel listing recent commits and
    // staged/changed files. File rows are clickable to open in an editor.
    public bool IsGitSidebarVisible { get; set; }

    public IReadOnlyList<GitSidebarLine>? GitSidebarCache { get; set; }

    public DateTimeOffset GitSidebarCacheTime { get; set; }

    // Click mapping from the last sidebar render: terminal row of the first
    // content line (-1 when hidden), panel width in columns, and the file path
    // (if any) at each content row.
    public int GitSidebarContentTopRow { get; set; } = -1;

    public int GitSidebarWidth { get; set; }

    public int GitSidebarSelectedIndex { get; set; } = -1;

    public int GitSidebarScrollOffset { get; set; }

    // Captured on the last sidebar render so the scroll handler can clamp without
    // re-running git.
    public int GitSidebarTotalLineCount { get; set; }

    public int GitSidebarViewportHeight { get; set; }

    public GitSidebarLine[] VisibleGitSidebarLines { get; set; } = [];

    public int GitSidebarCommitDisplayCount { get; set; } = 10;

    public string? SectionResumeCommand { get; set; }

    public string? SessionId { get; set; }

    public bool Running { get; set; } = true;

    public int SpinnerFrame { get; set; }

    public int? StreamingMessageId { get; set; }

    // Single running "files modified" tally, updated in place each turn (matches the VS surface).
    public ChatMessage? FileEditsSummaryMessage { get; set; }

    public Queue<char> StreamQueue { get; } = new();

    public long CurrentOperationId { get; private set; }

    public UiBridge UiBridge { get; }

    public bool HasInputSelection => TryGetInputSelectionRange(out _, out _);

    public void AddSystemMessage(string text)
    {
        AddMessage(Role.System, text);
    }

    public void AddThinkingMessage(string text)
    {
        AddMessage(Role.Thinking, text);
    }

    public ChatMessage AddMessage(Role role, string text)
    {
        ChatMessage message = new()
        {
            Id = _nextMessageId++,
            Role = role,
            Text = text
        };

        Messages.Add(message);
        return message;
    }

    public void ToggleThinkingMessage(int messageId)
    {
        if (!ExpandedThinkingMessageIds.Remove(messageId))
        {
            ExpandedThinkingMessageIds.Add(messageId);
        }
    }

    // True only when at least one thinking message exists and every one is expanded.
    public bool AreAllThinkingExpanded()
    {
        bool any = false;
        foreach (ChatMessage message in Messages)
        {
            if (message.Role != Role.Thinking)
            {
                continue;
            }

            any = true;
            if (!ExpandedThinkingMessageIds.Contains(message.Id))
            {
                return false;
            }
        }

        return any;
    }

    // Ctrl+T behaviour: collapse everything if all blocks are open, otherwise open all.
    public void ToggleAllThinking()
    {
        if (AreAllThinkingExpanded())
        {
            ExpandedThinkingMessageIds.Clear();
            return;
        }

        foreach (ChatMessage message in Messages)
        {
            if (message.Role == Role.Thinking)
            {
                ExpandedThinkingMessageIds.Add(message.Id);
            }
        }
    }

    public void BeginAssistantStream(string text)
    {
        string normalized = string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Trim();

        AppendAssistantStreamChunk(normalized);
    }

    public void AppendAssistantStreamChunk(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (StreamingMessageId is null || GetStreamingMessage() is null)
        {
            ChatMessage message = AddMessage(Role.Assistant, string.Empty);
            StreamingMessageId = message.Id;
            StreamQueue.Clear();
        }

        foreach (char character in text)
        {
            StreamQueue.Enqueue(character);
        }

        IsStreaming = true;
    }

    public ChatMessage? GetStreamingMessage()
    {
        return StreamingMessageId is null
            ? null
            : Messages.FirstOrDefault(message => message.Id == StreamingMessageId.Value);
    }

    public bool TryGetInputSelectionRange(out int startIndex, out int length)
    {
        startIndex = 0;
        length = 0;

        if (InputSelectionAnchor is not int anchor)
        {
            return false;
        }

        int cursorIndex = Math.Clamp(InputCursorIndex, 0, Input.Length);
        int normalizedAnchor = Math.Clamp(anchor, 0, Input.Length);
        if (cursorIndex == normalizedAnchor)
        {
            return false;
        }

        startIndex = Math.Min(cursorIndex, normalizedAnchor);
        length = Math.Abs(cursorIndex - normalizedAnchor);
        return length > 0;
    }

    public void ClearInputSelection()
    {
        InputSelectionAnchor = null;
    }

    public void ResetConversationViewport()
    {
        ConversationScrollOffset = 0;
        IsConversationAutoScrollPaused = false;
        LastConversationLineCount = 0;
    }

    public void JumpConversationToBottom()
    {
        ConversationScrollOffset = 0;
        IsConversationAutoScrollPaused = false;
    }

    public void SyncConversationAutoScrollPreference()
    {
        IsConversationAutoScrollPaused = ConversationScrollOffset > 0;
    }

    public long BeginTrackedOperation()
    {
        IsTurnInterruptPending = false;
        return CurrentOperationId = Interlocked.Increment(ref _nextOperationId);
    }

    public bool IsTrackedOperationCurrent(long operationId)
    {
        return operationId != 0 && CurrentOperationId == operationId;
    }

    public void AbandonTrackedOperation()
    {
        CurrentOperationId = Interlocked.Increment(ref _nextOperationId);
        ActiveOperation = null;
        IsTurnInterruptPending = false;
    }
}

public sealed record PendingSubmission(
    PendingSubmissionKind Kind,
    string Text,
    IReadOnlyList<ConversationAttachment>? Attachments = null);

public enum PendingSubmissionKind
{
    Prompt,
    Command
}
