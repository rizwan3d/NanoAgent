using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Storage;

namespace NanoAgent.Infrastructure.Mcp;

internal sealed class NanoAgentMcpConfigLoader
{
    private readonly IWorkspaceRootProvider _workspaceRootProvider;
    private readonly IUserDataPathProvider _userDataPathProvider;

    public NanoAgentMcpConfigLoader(
        IWorkspaceRootProvider workspaceRootProvider,
        IUserDataPathProvider userDataPathProvider)
    {
        _workspaceRootProvider = workspaceRootProvider;
        _userDataPathProvider = userDataPathProvider;
    }

    public IReadOnlyList<McpServerConfiguration> Load()
    {
        return AgentProfileConfigurationReader.LoadMcpServers(
            _userDataPathProvider,
            _workspaceRootProvider);
    }
}
