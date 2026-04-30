using NanoAgent.Application.Abstractions;

namespace NanoAgent.Infrastructure.Plugins.GitHub;

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
            new GitHubPluginTool(configuration, _httpClientFactory, GitHubPluginToolKind.Repository),
            new GitHubPluginTool(configuration, _httpClientFactory, GitHubPluginToolKind.Issue),
            new GitHubPluginTool(configuration, _httpClientFactory, GitHubPluginToolKind.PullRequest)
        ];
    }
}
