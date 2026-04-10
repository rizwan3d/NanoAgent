namespace NanoAgent;

internal interface IToolHandler
{
    string Name { get; }
    ChatToolDefinition Definition { get; }
    string Execute(ChatToolCall toolCall);
}
