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
        services.AddSingleton<IReplCommandHandler, BudgetCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ConfigCommandHandler>();
        services.AddSingleton<IReplCommandHandler, CloneCommandHandler>();
        services.AddSingleton<IReplCommandHandler, CompactCommandHandler>();
        services.AddSingleton<IReplCommandHandler, CopyCommandHandler>();
        services.AddSingleton<IReplCommandHandler, DenyCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ExportCommandHandler>();
        services.AddSingleton<IReplCommandHandler, HelpCommandHandler>();
        services.AddSingleton<IReplCommandHandler, InitCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ImportCommandHandler>();
        services.AddSingleton<IReplCommandHandler, McpCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ModelsCommandHandler>();
        services.AddSingleton<IReplCommandHandler, NewSessionCommandHandler>();
        services.AddSingleton<IReplCommandHandler, OnboardCommandHandler>();
        services.AddSingleton<IReplCommandHandler, PermissionsCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ProviderCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ProfileCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ForkCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ReloadCommandHandler>();
        services.AddSingleton<ResumeCommandHandler>();
        services.AddSingleton<IReplCommandHandler>(static serviceProvider =>
            serviceProvider.GetRequiredService<ResumeCommandHandler>());
        services.AddSingleton<IReplCommandHandler, ThinkingCommandHandler>();
        services.AddSingleton<IReplCommandHandler, SessionInfoCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ShareCommandHandler>();
        services.AddSingleton<IReplCommandHandler, TreeCommandHandler>();
        services.AddSingleton<IReplCommandHandler, UpdateCommandHandler>();
        services.AddSingleton<IReplCommandHandler, UndoCommandHandler>();
        services.AddSingleton<IReplCommandHandler, RedoCommandHandler>();
        services.AddSingleton<IReplCommandHandler, RulesCommandHandler>();
        services.AddSingleton<IReplCommandHandler, SettingCommandHandler>();
        services.AddSingleton<IReplCommandHandler, UseModelCommandHandler>();
        services.AddSingleton<IReplCommandHandler, ExitCommandHandler>();

        return services;
    }
}
