namespace NanoAgent;

internal sealed class ChatApplication
{
    private readonly IChatConsole _chatConsole;
    private readonly IAgentClient _agentClient;
    private readonly AppConfig _config;
    private readonly ChatSessionStore _sessionStore;

    public ChatApplication(IChatConsole chatConsole, IAgentClient agentClient, AppConfig config, ChatSessionStore sessionStore)
    {
        _chatConsole = chatConsole;
        _agentClient = agentClient;
        _config = config;
        _sessionStore = sessionStore;
    }

    public async Task RunAsync()
    {
        _chatConsole.RenderHeader(_config, _agentClient.SessionId, _agentClient.IsResumedSession, _sessionStore.ListRecent(5));
        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _chatConsole.RenderAgentMessage(
                $"Session closed. Session ID: {_agentClient.SessionId}\nRun again with `--session {_agentClient.SessionId}` to continue this conversation.");
            Environment.Exit(0);
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            while (true)
            {
                string? userInput = _chatConsole.ReadUserInput();
                if (string.IsNullOrWhiteSpace(userInput))
                {
                    continue;
                }

                if (IsSessionListCommand(userInput))
                {
                    _chatConsole.RenderSessionList(_sessionStore.ListRecent(10));
                    continue;
                }

                _chatConsole.RenderUserMessage(userInput);

                string agentResponse = await _agentClient.GetResponseAsync(userInput);
                _chatConsole.RenderAgentMessage(agentResponse);
            }
        }
        finally
        {
            if (cancelHandler is not null)
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
    }

    private static bool IsSessionListCommand(string userInput) =>
        string.Equals(userInput.Trim(), "/sessions", StringComparison.OrdinalIgnoreCase);
}
