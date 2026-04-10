namespace NanoAgent;

internal static class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Contains("--edit-global-config", StringComparer.OrdinalIgnoreCase))
        {
            AppConfigStore.EditGlobalConfig();
            return;
        }

        AppConfig config = AppConfigStore.Load();
        IChatConsole chatConsole = new CodeConsole();
        IAgentClient agentClient = new OpenAiCompatibleAgentClient(
            config.Endpoint,
            config.Model,
            new AgentPromptFactory(),
            new FileToolService());

        ChatApplication application = new ChatApplication(chatConsole, agentClient, config);
        await application.RunAsync();
    }
}
