using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using System.Text;

namespace NanoAgent.CLI;

public sealed class AppState
{
    private int _nextMessageId = 1;

    public AppState(UiBridge uiBridge, INanoAgentBackend backend)
    {
        UiBridge = uiBridge;
        Backend = backend;
    }

    public string? ActiveModelId { get; set; }

    public int? ActiveModelContextWindowTokens { get; set; }

    public Task? ActiveOperation { get; set; }

    public UiModalState? ActiveModal { get; set; }

    public string ActivityText { get; set; } = "Initializing NanoAgent backend";

    public INanoAgentBackend Backend { get; }

    public bool ClearBusyWhenStreamCompletes { get; set; }

    public bool HasFatalError { get; set; }

    public string? FatalExitMessage { get; set; }

    public StringBuilder Input { get; } = new();

    public List<CollapsedInputPaste> CollapsedInputPastes { get; } = [];

    public List<ConversationAttachment> InputAttachments { get; } = [];

    public int InputCursorIndex { get; set; }

    public bool SkipNextInputLineFeed { get; set; }

    public bool SlashCommandSuggestionsDismissed { get; set; }

    public int SlashCommandSuggestionIndex { get; set; }

    public bool IsBusy { get; set; }

    public bool IsReady { get; set; }

    public bool IsStreaming { get; set; }

    public bool IsPlanPinned { get; set; }

    public string? LatestPlanText { get; set; }

    public CancellationTokenSource LifetimeCancellation { get; } = new();

    public List<ChatMessage> Messages { get; } = [];

    public int ConversationScrollOffset { get; set; }

    public DateTimeOffset? CurrentTurnStartedAt { get; set; }

    public string? PendingCompletionNote { get; set; }

    public string? ProviderName { get; set; }

    public string RootDirectory { get; } = Directory.GetCurrentDirectory();

    public string? SectionResumeCommand { get; set; }

    public string? SessionId { get; set; }

    public bool Running { get; set; } = true;

    public int SpinnerFrame { get; set; }

    public int? StreamingMessageId { get; set; }

    public Queue<char> StreamQueue { get; } = new();

    public UiBridge UiBridge { get; }

    public void AddSystemMessage(string text)
    {
        AddMessage(Role.System, text);
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

    public void BeginAssistantStream(string text)
    {
        string normalized = string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Trim();

        ChatMessage message = AddMessage(Role.Assistant, string.Empty);
        StreamingMessageId = message.Id;
        StreamQueue.Clear();

        foreach (char character in normalized)
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
}
