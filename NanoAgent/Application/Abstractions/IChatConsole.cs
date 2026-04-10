namespace NanoAgent;

internal interface IChatConsole
{
    void RenderHeader(AppConfig config);
    string? ReadUserInput();
    void RenderUserMessage(string userInput);
    void RenderCommandMessage(string command);
    void RenderAgentMessage(string message);
    void RenderVerboseMessage(string message);
}
