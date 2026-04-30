using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Plugins;

namespace NanoAgent.Plugin.GitHub;

internal sealed class GitHubPluginToolFactory : IPluginToolFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public GitHubPluginToolFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string PluginName => GitHubPluginTool.PluginName;

    public IReadOnlyList<ITool> CreateTools(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return
        [
            .. GitHubPluginToolKind.All.Select(kind => new GitHubPluginTool(
                configuration,
                _httpClientFactory,
                kind))
        ];
    }
}
