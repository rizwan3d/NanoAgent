using FinalAgent.Application.Abstractions;
using FinalAgent.ConsoleHost.Hosting;
using FinalAgent.ConsoleHost.Prompts;
using FinalAgent.ConsoleHost.Repl;
using FinalAgent.ConsoleHost.Terminal;
using Microsoft.Extensions.DependencyInjection;

namespace FinalAgent.ConsoleHost.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConsoleHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IConsoleTerminal, ConsoleTerminal>();
        services.AddSingleton<IConsolePromptRenderer, ConsolePromptRenderer>();
        services.AddSingleton<IConsolePromptInputReader, ConsolePromptInputReader>();
        services.AddSingleton<ISelectionPrompt, ConsoleSelectionPrompt>();
        services.AddSingleton<ITextPrompt, ConsoleTextPrompt>();
        services.AddSingleton<ISecretPrompt, ConsoleSecretPrompt>();
        services.AddSingleton<IConfirmationPrompt, ConsoleConfirmationPrompt>();
        services.AddSingleton<IStatusMessageWriter, ConsoleStatusMessageWriter>();
        services.AddSingleton<IReplInputReader, ConsoleReplInputReader>();
        services.AddSingleton<IReplOutputWriter, ConsoleReplOutputWriter>();
        services.AddSingleton<ProcessExitCodeTracker>();
        services.AddHostedService<ConsoleApplicationHostedService>();

        return services;
    }
}
