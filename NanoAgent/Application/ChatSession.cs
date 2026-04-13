namespace NanoAgent;

internal sealed class ChatSession : IChatSession
{
    private readonly int _maxSessionMessages;
    private readonly int _maxSessionEstimatedTokens;
    private readonly ChatSessionStore _store;
    private readonly DateTimeOffset _createdAtUtc;
    private readonly List<ChatMessage> _messages;

    public ChatSession(
        string systemPrompt,
        int maxSessionMessages,
        int maxSessionEstimatedTokens,
        string sessionId,
        ChatSessionStore store,
        bool resumeExisting)
    {
        _maxSessionMessages = Math.Max(4, maxSessionMessages);
        _maxSessionEstimatedTokens = Math.Max(1024, maxSessionEstimatedTokens);
        _store = store;
        SessionId = sessionId;

        if (resumeExisting)
        {
            ChatSessionRecord record = _store.Load(sessionId);
            _createdAtUtc = record.CreatedAtUtc;
            _messages = CloneMessages(record.Messages);
            EnsureSystemPrompt(systemPrompt);
            IsResumedSession = true;
            TrimHistory(_messages);
            Save();
            return;
        }

        _createdAtUtc = DateTimeOffset.UtcNow;
        _messages =
        [
            new ChatMessage
            {
                Role = ChatRole.System,
                Content = systemPrompt
            }
        ];
        IsResumedSession = false;
        Save();
    }

    public string SessionId { get; }

    public bool IsResumedSession { get; }

    public List<ChatMessage> CreateTurnMessages(string userPrompt)
    {
        List<ChatMessage> messages = CloneMessages(_messages);
        messages.Add(new ChatMessage
        {
            Role = ChatRole.User,
            Content = userPrompt
        });
        TrimHistory(messages);
        return messages;
    }

    public void CommitTurn(List<ChatMessage> messages)
    {
        _messages.Clear();
        List<ChatMessage> committed = CloneMessages(messages);
        TrimHistory(committed);
        _messages.AddRange(committed);
        Save();
    }

    private void TrimHistory(List<ChatMessage> messages)
    {
        while (messages.Count > _maxSessionMessages || EstimateTokens(messages) > _maxSessionEstimatedTokens)
        {
            if (messages.Count <= 2)
            {
                break;
            }

            messages.RemoveAt(1);
        }
    }

    private static int EstimateTokens(IEnumerable<ChatMessage> messages)
    {
        int characterCount = 0;

        foreach (ChatMessage message in messages)
        {
            characterCount += message.Role.Length;
            characterCount += message.Content?.Length ?? 0;
            characterCount += message.ToolCallId?.Length ?? 0;

            if (message.ToolCalls is null)
            {
                continue;
            }

            foreach (ChatToolCall toolCall in message.ToolCalls)
            {
                characterCount += toolCall.Id.Length;
                characterCount += toolCall.Type.Length;
                characterCount += toolCall.Function.Name.Length;
                characterCount += toolCall.Function.Arguments.Length;
            }
        }

        return Math.Max(1, (int)Math.Ceiling(characterCount / 4d));
    }

    private static List<ChatMessage> CloneMessages(IEnumerable<ChatMessage> messages) =>
        messages.Select(CloneMessage).ToList();

    private void EnsureSystemPrompt(string systemPrompt)
    {
        if (_messages.Count == 0 || !string.Equals(_messages[0].Role, ChatRole.System, StringComparison.Ordinal))
        {
            _messages.Insert(0, new ChatMessage
            {
                Role = ChatRole.System,
                Content = systemPrompt
            });
            return;
        }

        _messages[0].Content = systemPrompt;
    }

    private void Save()
    {
        _store.Save(new ChatSessionRecord
        {
            SessionId = SessionId,
            CreatedAtUtc = _createdAtUtc,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Messages = CloneMessages(_messages).ToArray()
        });
    }

    private static ChatMessage CloneMessage(ChatMessage message) =>
        new()
        {
            Role = message.Role,
            Content = message.Content,
            ToolCallId = message.ToolCallId,
            ToolCalls = message.ToolCalls?.Select(CloneToolCall).ToArray()
        };

    private static ChatToolCall CloneToolCall(ChatToolCall toolCall) =>
        new()
        {
            Id = toolCall.Id,
            Type = toolCall.Type,
            Function = new ChatToolFunctionCall
            {
                Name = toolCall.Function.Name,
                Arguments = toolCall.Function.Arguments
            }
        };
}
