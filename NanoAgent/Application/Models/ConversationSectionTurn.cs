namespace NanoAgent.Application.Models;

public sealed class ConversationSectionTurn
{
    public ConversationSectionTurn(
        string userInput,
        string assistantResponse,
        IReadOnlyList<ConversationToolCall>? toolCalls = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(assistantResponse);

        UserInput = userInput.Trim();
        AssistantResponse = assistantResponse.Trim();
        ToolCalls = (toolCalls ?? [])
            .Where(static toolCall =>
                toolCall is not null &&
                !string.IsNullOrWhiteSpace(toolCall.Id) &&
                !string.IsNullOrWhiteSpace(toolCall.Name) &&
                !string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
            .Select(static toolCall => new ConversationToolCall(
                toolCall.Id.Trim(),
                toolCall.Name.Trim(),
                toolCall.ArgumentsJson.Trim()))
            .ToArray();
    }

    public string AssistantResponse { get; }

    public IReadOnlyList<ConversationToolCall> ToolCalls { get; }

    public string UserInput { get; }
}
