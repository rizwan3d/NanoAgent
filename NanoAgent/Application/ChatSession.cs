namespace NanoAgent;

internal sealed class ChatSession : IChatSession
{
    private readonly List<ChatMessage> _messages;

    public ChatSession(string systemPrompt)
    {
        _messages =
        [
            new ChatMessage
            {
                Role = ChatRole.System,
                Content = systemPrompt
            }
        ];
    }

    public List<ChatMessage> CreateTurnMessages(string userPrompt)
    {
        List<ChatMessage> messages = CloneMessages(_messages);
        messages.Add(new ChatMessage
        {
            Role = ChatRole.User,
            Content = userPrompt
        });
        return messages;
    }

    public void CommitTurn(List<ChatMessage> messages)
    {
        _messages.Clear();
        _messages.AddRange(CloneMessages(messages));
    }

    private static List<ChatMessage> CloneMessages(IEnumerable<ChatMessage> messages) =>
        messages.Select(CloneMessage).ToList();

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
