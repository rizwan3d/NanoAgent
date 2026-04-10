namespace NanoAgent;

internal interface IChatConsole
{
    void RenderHeader(AppConfig config);
    string? ReadUserInput();
    void RenderUserMessage(string userInput);
    void RenderCommandMessage(string command);
    void BeginAgentActivity();
    void UpdateAgentActivity(TimeSpan elapsed, int? outputTokens, bool isEstimate);
    void CompleteAgentActivity(TimeSpan elapsed, int? outputTokens, bool isEstimate);
    void RenderAgentMessage(string message);
    void RenderVerboseMessage(string message);
}
