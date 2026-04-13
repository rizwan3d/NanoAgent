namespace NanoAgent;

internal static class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        AppRuntimeOptions runtimeOptions = new(
            Verbose: args.Contains("--verbose", StringComparer.OrdinalIgnoreCase));

        if (args.Contains("--edit-global-config", StringComparer.OrdinalIgnoreCase))
        {
            AppConfigStore.EditGlobalConfig();
            return;
        }

        AppConfig config = AppConfigStore.Load();
        IChatConsole chatConsole = new CodeConsole();
        AgentPromptFactory promptFactory = new();
        IToolService toolService = new FileToolService();
        IChatSession chatSession = new ChatSession(promptFactory.CreateSystemPrompt());
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
}
