namespace NanoAgent;

internal interface IAgentClient
{
    string SessionId { get; }
    bool IsResumedSession { get; }
    Task<string> GetResponseAsync(string userPrompt);
}
