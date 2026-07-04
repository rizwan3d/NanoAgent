using NanoAgent.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace NanoAgent.Application.Tools;

internal static class ToolCatalog
{
    private static readonly Action<IServiceCollection>[] Registrations =
    [
        static services => services.AddSingleton<ITool, AgentDelegateTool>(),
        static services => services.AddSingleton<ITool, AgentOrchestrateTool>(),
        static services => services.AddSingleton<ITool, ApplyPatchTool>(),
        static services => services.AddSingleton<ITool, AskQuestionTool>(),
        static services => services.AddSingleton<ITool, CodebaseIndexTool>(),
        static services => services.AddSingleton<ITool, CodeIntelligenceTool>(),
        static services => services.AddSingleton<ITool, FileDeleteTool>(),
        static services => services.AddSingleton<ITool, FileReadTool>(),
        static services => services.AddSingleton<ITool, DirectoryListTool>(),
        static services => services.AddSingleton<ITool, HeadlessBrowserTool>(),
        static services => services.AddSingleton<ITool, PlanningModeTool>(),
        static services => services.AddSingleton<ITool, RepoMemoryTool>(),
        static services => services.AddSingleton<ITool, SearchFilesTool>(),
        static services => services.AddSingleton<ITool, FileWriteTool>(),
        static services => services.AddSingleton<ITool, SkillLoadTool>(),
        static services => services.AddSingleton<ITool, TextSearchTool>(),
        static services => services.AddSingleton<ITool, UpdatePlanTool>(),
        static services => services.AddSingleton<ITool, WebSearchTool>(),
        static services => services.AddSingleton<ITool, ShellCommandTool>()
    ];

    public static IServiceCollection AddRegisteredTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (Action<IServiceCollection> registration in Registrations)
        {
            registration(services);
        }

        return services;
    }
}
