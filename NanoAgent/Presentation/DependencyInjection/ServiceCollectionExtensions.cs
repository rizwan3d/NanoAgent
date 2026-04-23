using NanoAgent.Presentation.Abstractions;
using NanoAgent.Presentation.Repl.Commands;
using NanoAgent.Presentation.Repl.Parsing;
using NanoAgent.Presentation.Repl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace NanoAgent.Presentation.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IReplRuntime, ReplRuntime>();
        services.AddSingleton<IReplCommandParser, ReplCommandParser>();
        services.AddSingleton<IReplCommandDispatcher, ReplCommandDispatcher>();
        services.AddSingleton<IReplCommandHandler, AllowCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ConfigCommandHandler>();
        services.AddSingleton<IReplCommandHandler, DenyCommandHandler>();
        services.AddSingleton<IReplCommandHandler, HelpCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ModelsCommandHandler>();
        services.AddSingleton<IReplCommandHandler, PermissionsCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ProfileCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ThinkingCommandHandler>();
        services.AddSingleton<IReplCommandHandler, UndoCommandHandler>();
        services.AddSingleton<IReplCommandHandler, RedoCommandHandler>();
        services.AddSingleton<IReplCommandHandler, RulesCommandHandler>();
        services.AddSingleton<IReplCommandHandler, UseModelCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ExitCommandHandler>();

        return services;
    }
}
