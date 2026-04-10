namespace NanoAgent;

internal sealed class ChatApplication
{
    private readonly IChatConsole _chatConsole;
    private readonly IAgentClient _agentClient;
    private readonly AppConfig _config;

    public ChatApplication(IChatConsole chatConsole, IAgentClient agentClient, AppConfig config)
    {
        _chatConsole = chatConsole;
        _agentClient = agentClient;
        _config = config;
    }

    public async Task RunAsync()
    {
        _chatConsole.RenderHeader(_config);

        while (true)
        {
            string? userInput = _chatConsole.ReadUserInput();
            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            _chatConsole.RenderUserMessage(userInput);

            string agentResponse = await _agentClient.GetResponseAsync(userInput);
            _chatConsole.RenderAgentMessage(agentResponse);
        }
    }
}
