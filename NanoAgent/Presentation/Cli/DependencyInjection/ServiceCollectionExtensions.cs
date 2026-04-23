using NanoAgent.Application.Abstractions;
using NanoAgent.Presentation.Abstractions;
using NanoAgent.Presentation.Cli.Hosting;
using NanoAgent.Presentation.Cli.Prompts;
using NanoAgent.Presentation.Cli.Repl;
using NanoAgent.Presentation.Cli.Rendering;
using NanoAgent.Presentation.Cli.Terminal;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace NanoAgent.Presentation.Cli.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCliPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IApplicationRunner, AgentApplicationRunner>();
        services.AddSingleton<IConsoleInteractionGate, ConsoleInteractionGate>();
        services.AddSingleton<IConsoleTerminal, ConsoleTerminal>();
        services.AddSingleton<IAnsiConsole>(static serviceProvider =>
            SpectreConsoleFactory.Create(serviceProvider.GetRequiredService<IConsoleTerminal>()));
        services.AddSingleton(static serviceProvider =>
        {
            IConsoleTerminal terminal = serviceProvider.GetRequiredService<IConsoleTerminal>();

            return new ConsoleRenderSettings
            {
                EnableAnimations = !terminal.IsOutputRedirected &&
                    !string.Equals(
                        Environment.GetEnvironmentVariable("NANOAGENT_DISABLE_ANIMATIONS"),
                        "1",
                        StringComparison.Ordinal)
            };
        });
        services.AddSingleton<IConsolePromptRenderer, ConsolePromptRenderer>();
        services.AddSingleton<IConsolePromptInputReader, ConsolePromptInputReader>();
        services.AddSingleton<ICliOutputTarget, ConsoleCliOutputTarget>();
        services.AddSingleton<ICliMessageFormatter, MarkdownLikeCliMessageFormatter>();
        services.AddSingleton<ICliTextRenderer, CliTextRenderer>();
        services.AddSingleton<ISelectionPrompt, ConsoleSelectionPrompt>();
        services.AddSingleton<ITextPrompt, ConsoleTextPrompt>();
        services.AddSingleton<ISecretPrompt, ConsoleSecretPrompt>();
        services.AddSingleton<IConfirmationPrompt, ConsoleConfirmationPrompt>();
        services.AddSingleton<IStatusMessageWriter, ConsoleStatusMessageWriter>();
        services.AddSingleton<IReplInputReader, ConsoleReplInputReader>();
        services.AddSingleton<IReplInterruptMonitor, ConsoleReplInterruptMonitor>();
        services.AddSingleton<IReplOutputWriter, ConsoleReplOutputWriter>();
        services.AddSingleton<ProcessExitCodeTracker>();
        services.AddHostedService<ConsoleApplicationHostedService>();

        return services;
    }
}
