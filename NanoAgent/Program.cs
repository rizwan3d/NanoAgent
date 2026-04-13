namespace NanoAgent;

internal static class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        IChatConsole chatConsole = new CodeConsole();
        string? sessionIdArgument;
        AppRuntimeOptions runtimeOptions = new(
            Verbose: args.Contains("--verbose", StringComparer.OrdinalIgnoreCase));

        try
        {
            sessionIdArgument = TryGetOptionValue(args, "--session");

            if (args.Contains("--edit-global-config", StringComparer.OrdinalIgnoreCase))
            {
                AppConfigStore.EditGlobalConfig();
                return;
            }

            AppConfig config = AppConfigStore.Load();
            config.Validate();

            AgentPromptFactory promptFactory = new();
            IToolService toolService = new FileToolService();
            ChatSessionStore sessionStore = new(AppConfigStore.GetSessionsDirectoryPath());
            string sessionId = string.IsNullOrWhiteSpace(sessionIdArgument)
                ? ChatSessionStore.CreateSessionId()
                : sessionIdArgument.Trim();
            IChatSession chatSession = new ChatSession(
                promptFactory.CreateSystemPrompt(),
                config.MaxSessionMessages,
                config.MaxSessionEstimatedTokens,
                sessionId,
                sessionStore,
                resumeExisting: !string.IsNullOrWhiteSpace(sessionIdArgument));
            IAgentClient agentClient = new OpenAiCompatibleAgentClient(
                config.Endpoint,
                config.Model,
                config.ApiKey,
                toolService,
                chatSession,
                chatConsole,
                runtimeOptions);

            ChatApplication application = new ChatApplication(chatConsole, agentClient, config);
            await application.RunAsync();
        }
        catch (Exception exception)
        {
            chatConsole.RenderAgentMessage($"Startup error: {exception.Message}");
            return;
        }
    }

    private static string? TryGetOptionValue(string[] args, string optionName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                throw new InvalidOperationException($"Missing value for {optionName}.");
            }

            return args[i + 1];
        }

        return null;
    }
}
