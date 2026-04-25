using Microsoft.Extensions.DependencyInjection;

namespace NanoAgent.Application.Commands;

public static class CommandServiceCollectionExtensions
{
    public static IServiceCollection AddReplCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IReplCommandParser, ReplCommandParser>();
        services.AddSingleton<IReplCommandDispatcher, ReplCommandDispatcher>();
        services.AddSingleton<IReplCommandHandler, AllowCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ConfigCommandHandler>();
        services.AddSingleton<IReplCommandHandler, DenyCommandHandler>();
        services.AddSingleton<IReplCommandHandler, HelpCommandHandler>();
        services.AddSingleton<IReplCommandHandler, InitCommandHandler>();
        services.AddSingleton<IReplCommandHandler, McpCommandHandler>();
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
