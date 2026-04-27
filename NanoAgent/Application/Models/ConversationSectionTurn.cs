using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Models;

public sealed class ConversationSectionTurn
{
    public ConversationSectionTurn(
        string userInput,
        string assistantResponse,
        IReadOnlyList<ConversationToolCall>? toolCalls = null,
        IReadOnlyList<string>? toolOutputMessages = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(assistantResponse);

        UserInput = SecretRedactor.Redact(userInput.Trim());
        AssistantResponse = SecretRedactor.Redact(assistantResponse.Trim());
        ToolCalls = (toolCalls ?? [])
            .Where(static toolCall =>
                toolCall is not null &&
                !string.IsNullOrWhiteSpace(toolCall.Id) &&
                !string.IsNullOrWhiteSpace(toolCall.Name) &&
                !string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
            .Select(static toolCall => new ConversationToolCall(
                toolCall.Id.Trim(),
                toolCall.Name.Trim(),
                SecretRedactor.Redact(toolCall.ArgumentsJson.Trim())))
            .ToArray();
        ToolOutputMessages = (toolOutputMessages ?? [])
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Select(static message => SecretRedactor.Redact(message.Trim()))
            .ToArray();
    }

    public string AssistantResponse { get; }

    public IReadOnlyList<ConversationToolCall> ToolCalls { get; }

    public IReadOnlyList<string> ToolOutputMessages { get; }

    public string UserInput { get; }
}
