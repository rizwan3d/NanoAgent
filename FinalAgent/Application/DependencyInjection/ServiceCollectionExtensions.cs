using FinalAgent.Application.Abstractions;
using FinalAgent.Application.Repl.Commands;
using FinalAgent.Application.Repl.Services;
using FinalAgent.Application.Services;
using FinalAgent.Domain.Abstractions;
using FinalAgent.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FinalAgent.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IApplicationRunner, AgentApplicationRunner>();
        services.AddSingleton<IReplRuntime, ReplRuntime>();
        services.AddSingleton<IReplCommandDispatcher, ReplCommandDispatcher>();
        services.AddSingleton<IConversationPipeline, AgentConversationPipeline>();
        services.AddSingleton<IReplCommandHandler, HelpCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ExitCommandHandler>();
        services.AddSingleton<IModelDiscoveryService, ModelDiscoveryService>();
        services.AddSingleton<IFirstRunOnboardingService, FirstRunOnboardingService>();
        services.AddSingleton<IOnboardingInputValidator, OnboardingInputValidator>();
        services.AddSingleton<IAgentProviderProfileFactory, AgentProviderProfileFactory>();
        services.AddSingleton<IModelSelectionPolicy, RankedModelSelectionPolicy>();

        return services;
    }
}
