namespace NanoAgent.Application.Models;

public sealed class ConversationSectionTurn
{
    public ConversationSectionTurn(
        string userInput,
        string assistantResponse)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(assistantResponse);

        UserInput = userInput.Trim();
        AssistantResponse = assistantResponse.Trim();
    }

    public string AssistantResponse { get; }

    public string UserInput { get; }
}
