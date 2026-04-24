using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Infrastructure.Mcp;

internal sealed class NanoAgentMcpConfigLoader
{
    private const string WorkspaceConfigurationDirectoryName = ".nanoagent";
    private const string WorkspaceConfigurationFileName = "config.toml";

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
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        Dictionary<string, McpServerConfiguration> servers = new(StringComparer.OrdinalIgnoreCase);

        foreach (string configPath in GetConfigPaths(workspaceRoot))
        {
            if (!File.Exists(configPath))
            {
                continue;
            }

            foreach (McpServerConfiguration server in NanoAgentMcpTomlParser.Parse(configPath))
            {
                server.ResolveRelativePaths(workspaceRoot);
                if (!servers.TryGetValue(server.Name, out McpServerConfiguration? existing))
                {
                    servers[server.Name] = server;
                    continue;
                }

                existing.Merge(server);
            }
        }

        return servers.Values
            .OrderBy(static server => server.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<string> GetConfigPaths(string workspaceRoot)
    {
        List<string> paths = [];
        string userConfigPath = _userDataPathProvider.GetMcpConfigurationFilePath();
        if (!string.IsNullOrWhiteSpace(userConfigPath))
        {
            paths.Add(Path.GetFullPath(userConfigPath));
        }

        string workspaceConfigPath = WorkspacePath.Resolve(
            workspaceRoot,
            Path.Combine(WorkspaceConfigurationDirectoryName, WorkspaceConfigurationFileName));
        if (!paths.Contains(workspaceConfigPath, StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(workspaceConfigPath);
        }

        return paths;
    }
}
