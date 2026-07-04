using Microsoft.Extensions.DependencyInjection;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Conversation.Services;
using NanoAgent.Application.Formatting;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Services;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Services;
using NanoAgent.Domain.Abstractions;
using NanoAgent.Domain.Services;

namespace NanoAgent.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAgentProfileResolver, BuiltInAgentProfileResolver>();
        services.AddSingleton<IProductTelemetry, NoOpProductTelemetry>();
        services.AddSingleton<AgentTurnService>();
        services.AddSingleton<ISessionAppService, SessionAppService>();
        services.AddSingleton<IConversationPipeline, AgentConversationPipeline>();
        services.AddSingleton<IToolOutputFormatter, ToolOutputFormatter>();
        services.AddSingleton<ILifecycleHookService, NoOpLifecycleHookService>();
        services.AddSingleton<ToolPermissionParser>();
        services.AddSingleton<ToolPermissionEvaluator>();
        services.AddSingleton<IPermissionApprovalPrompt, SelectionPermissionApprovalPrompt>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolInvoker, RegistryBackedToolInvoker>();
        services.AddSingleton<IToolExecutionPipeline, ToolExecutionPipeline>();
        services.AddRegisteredTools();
        services.AddSingleton<IModelDiscoveryService, ModelDiscoveryService>();
        services.AddSingleton<IFirstRunOnboardingService, FirstRunOnboardingService>();
        services.AddSingleton<IProviderSetupService, ProviderSetupService>();
        services.AddSingleton<IOnboardingInputValidator, OnboardingInputValidator>();
        services.AddSingleton<IModelActivationService, ModelActivationService>();
        services.AddSingleton<IInteractiveReasoningSelectionService, InteractiveReasoningSelectionService>();
        services.AddSingleton<IInteractiveModelSelectionService, InteractiveModelSelectionService>();
        services.AddSingleton<IReplSectionService, ReplSectionService>();
        services.AddSingleton<ITokenEstimator, HeuristicTokenEstimator>();
        services.AddSingleton<IAgentProviderProfileFactory, AgentProviderProfileFactory>();
        services.AddSingleton<IModelSelectionPolicy, ConfiguredOrFirstModelSelectionPolicy>();

        return services;
    }

    private sealed class NoOpProductTelemetry : IProductTelemetry
    {
        public void TrackAppStarted()
        {
        }

        public void TrackAppStopped()
        {
        }

        public void TrackFeatureUsed(
            string featureName,
            string interactionKind,
            bool success,
            Models.ConversationTurnMetrics? metrics = null,
            int attachmentCount = 0,
            Exception? exception = null)
        {
        }
    }
}
