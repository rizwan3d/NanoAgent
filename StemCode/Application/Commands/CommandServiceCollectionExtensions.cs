using Microsoft.Extensions.DependencyInjection;

namespace StemCode.Application.Commands;

public static class CommandServiceCollectionExtensions
{
    public static IServiceCollection AddReplCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IReplCommandParser, ReplCommandParser>();
        services.AddSingleton<IReplCommandDispatcher, ReplCommandDispatcher>();
        services.AddRegisteredReplCommandHandlers();

        return services;
    }
}
