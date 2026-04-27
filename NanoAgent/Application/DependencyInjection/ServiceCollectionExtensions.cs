using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Conversation.Services;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Services;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Services;
using NanoAgent.Domain.Abstractions;
using NanoAgent.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace NanoAgent.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAgentProfileResolver, BuiltInAgentProfileResolver>();
        services.AddSingleton<IAgentTurnService, AgentTurnService>();
        services.AddSingleton<ISessionAppService, SessionAppService>();
        services.AddSingleton<IConversationPipeline, AgentConversationPipeline>();
        services.AddSingleton<ILifecycleHookService, NoOpLifecycleHookService>();
        services.AddSingleton<IPermissionParser, ToolPermissionParser>();
        services.AddSingleton<IPermissionEvaluator, ToolPermissionEvaluator>();
        services.AddSingleton<IPermissionApprovalPrompt, SelectionPermissionApprovalPrompt>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolInvoker, RegistryBackedToolInvoker>();
        services.AddSingleton<IToolExecutionPipeline, ToolExecutionPipeline>();
        services.AddSingleton<ITool, AgentDelegateTool>();
        services.AddSingleton<ITool, AgentOrchestrateTool>();
        services.AddSingleton<ITool, ApplyPatchTool>();
        services.AddSingleton<ITool, CodeIntelligenceTool>();
        services.AddSingleton<ITool, FileDeleteTool>();
        services.AddSingleton<ITool, FileReadTool>();
        services.AddSingleton<ITool, DirectoryListTool>();
        services.AddSingleton<ITool, HeadlessBrowserTool>();
        services.AddSingleton<ITool, LessonMemoryTool>();
        services.AddSingleton<ITool, PlanningModeTool>();
        services.AddSingleton<ITool, SearchFilesTool>();
        services.AddSingleton<ITool, FileWriteTool>();
        services.AddSingleton<ITool, SkillLoadTool>();
        services.AddSingleton<ITool, TextSearchTool>();
        services.AddSingleton<ITool, UpdatePlanTool>();
        services.AddSingleton<ITool, WebRunTool>();
        services.AddSingleton<ITool, ShellCommandTool>();
        services.AddSingleton<IModelDiscoveryService, ModelDiscoveryService>();
        services.AddSingleton<IFirstRunOnboardingService, FirstRunOnboardingService>();
        services.AddSingleton<IProviderSetupService, ProviderSetupService>();
        services.AddSingleton<IOnboardingInputValidator, OnboardingInputValidator>();
        services.AddSingleton<IModelActivationService, ModelActivationService>();
        services.AddSingleton<IReplSectionService, ReplSectionService>();
        services.AddSingleton<ITokenEstimator, HeuristicTokenEstimator>();
        services.AddSingleton<IAgentProviderProfileFactory, AgentProviderProfileFactory>();
        services.AddSingleton<IModelSelectionPolicy, ConfiguredOrFirstModelSelectionPolicy>();

        return services;
    }
}
