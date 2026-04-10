namespace NanoAgent;

internal interface IChatSession
{
    List<ChatMessage> CreateTurnMessages(string userPrompt);
    void CommitTurn(List<ChatMessage> messages);
}
