namespace NanoAgent;

internal interface IAgentClient
{
    Task<string> GetResponseAsync(string userPrompt);
}
