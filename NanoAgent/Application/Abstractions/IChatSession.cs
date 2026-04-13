namespace NanoAgent;

internal interface IChatSession
{
    string SessionId { get; }
    bool IsResumedSession { get; }
    List<ChatMessage> CreateTurnMessages(string userPrompt);
    void CommitTurn(List<ChatMessage> messages);
}
