namespace NanoAgent;

internal interface IToolService
{
    ChatToolDefinition[] GetToolDefinitions();
    string Execute(ChatToolCall toolCall);
}
