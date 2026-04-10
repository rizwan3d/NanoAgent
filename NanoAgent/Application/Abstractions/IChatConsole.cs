namespace NanoAgent;

internal interface IChatConsole
{
    void RenderHeader(AppConfig config);
    string? ReadUserInput();
    void RenderUserMessage(string userInput);
    void RenderAgentMessage(string message);
}
