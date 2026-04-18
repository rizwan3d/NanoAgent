using FinalAgent.ConsoleHost.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FinalAgent.ConsoleHost.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConsoleHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ProcessExitCodeTracker>();
        services.AddHostedService<ConsoleApplicationHostedService>();

        return services;
    }
}
