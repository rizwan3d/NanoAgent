using Microsoft.Extensions.DependencyInjection;
using NanoAgent.Infrastructure.Plugins;

namespace NanoAgent.Plugin.GitHub;

public static class ServiceCollectionExtensions
{
    internal const string HttpClientName = "NanoAgent.Plugin.GitHub";

    public static IServiceCollection AddGitHubPlugin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPluginToolFactory, GitHubPluginToolFactory>();
        services.AddHttpClient(HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NanoAgent/1.0");
        });

        return services;
    }
}
