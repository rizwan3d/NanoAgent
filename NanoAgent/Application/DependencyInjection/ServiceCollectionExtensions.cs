using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Conversation.Services;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Repl.Commands;
using NanoAgent.Application.Repl.Parsing;
using NanoAgent.Application.Repl.Services;
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

        services.AddScoped<IApplicationRunner, AgentApplicationRunner>();
        services.AddSingleton<IReplRuntime, ReplRuntime>();
        services.AddSingleton<IReplCommandParser, ReplCommandParser>();
        services.AddSingleton<IReplCommandDispatcher, ReplCommandDispatcher>();
        services.AddSingleton<IAgentProfileResolver, BuiltInAgentProfileResolver>();
        services.AddSingleton<IAgentTurnService, AgentTurnService>();
        services.AddSingleton<ISessionAppService, SessionAppService>();
        services.AddSingleton<IConversationPipeline, AgentConversationPipeline>();
        services.AddSingleton<IPermissionParser, ToolPermissionParser>();
        services.AddSingleton<IPermissionEvaluator, ToolPermissionEvaluator>();
        services.AddSingleton<IPermissionApprovalPrompt, SelectionPermissionApprovalPrompt>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolInvoker, RegistryBackedToolInvoker>();
        services.AddSingleton<IToolExecutionPipeline, ToolExecutionPipeline>();
        services.AddSingleton<ITool, ApplyPatchTool>();
        services.AddSingleton<ITool, FileReadTool>();
        services.AddSingleton<ITool, DirectoryListTool>();
        services.AddSingleton<ITool, PlanningModeTool>();
        services.AddSingleton<ITool, SearchFilesTool>();
        services.AddSingleton<ITool, FileWriteTool>();
        services.AddSingleton<ITool, TextSearchTool>();
        services.AddSingleton<ITool, UpdatePlanTool>();
        services.AddSingleton<ITool, WebSearchTool>();
        services.AddSingleton<ITool, ShellCommandTool>();
        services.AddSingleton<IReplCommandHandler, AllowCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ConfigCommandHandler>();
        services.AddSingleton<IReplCommandHandler, DenyCommandHandler>();
        services.AddSingleton<IReplCommandHandler, HelpCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ModelsCommandHandler>();
        services.AddSingleton<IReplCommandHandler, PermissionsCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ProfileCommandHandler>();
        services.AddSingleton<IReplCommandHandler, UndoCommandHandler>();
        services.AddSingleton<IReplCommandHandler, RedoCommandHandler>();
        services.AddSingleton<IReplCommandHandler, RulesCommandHandler>();
        services.AddSingleton<IReplCommandHandler, UseModelCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ExitCommandHandler>();
        services.AddSingleton<IModelDiscoveryService, ModelDiscoveryService>();
        services.AddSingleton<IFirstRunOnboardingService, FirstRunOnboardingService>();
        services.AddSingleton<IOnboardingInputValidator, OnboardingInputValidator>();
        services.AddSingleton<IModelActivationService, ModelActivationService>();
        services.AddSingleton<IReplSectionService, ReplSectionService>();
        services.AddSingleton<ITokenEstimator, HeuristicTokenEstimator>();
        services.AddSingleton<IAgentProviderProfileFactory, AgentProviderProfileFactory>();
        services.AddSingleton<IModelSelectionPolicy, ConfiguredOrFirstModelSelectionPolicy>();

        return services;
    }
}
