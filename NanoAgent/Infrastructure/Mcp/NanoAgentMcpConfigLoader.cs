using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Backend;
using NanoAgent.Infrastructure.Storage;

namespace NanoAgent.Infrastructure.Mcp;

internal sealed class NanoAgentMcpConfigLoader
{
    private const string AcpSourcePrefix = "ACP ";

    private readonly IReadOnlyList<BackendMcpServerConfiguration> _sessionMcpServers;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;
    private readonly IUserDataPathProvider _userDataPathProvider;

    public NanoAgentMcpConfigLoader(
        IWorkspaceRootProvider workspaceRootProvider,
        IUserDataPathProvider userDataPathProvider,
        IReadOnlyList<BackendMcpServerConfiguration>? sessionMcpServers = null)
    {
        _workspaceRootProvider = workspaceRootProvider;
        _userDataPathProvider = userDataPathProvider;
        _sessionMcpServers = sessionMcpServers ?? [];
    }

    public IReadOnlyList<McpServerConfiguration> Load()
    {
        IReadOnlyList<McpServerConfiguration> configuredServers = AgentProfileConfigurationReader.LoadMcpServers(
            _userDataPathProvider,
            _workspaceRootProvider);
        if (_sessionMcpServers.Count == 0)
        {
            return configuredServers;
        }

        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        Dictionary<string, McpServerConfiguration> servers = configuredServers.ToDictionary(
            static server => server.Name,
            static server => server,
            StringComparer.OrdinalIgnoreCase);

        foreach (BackendMcpServerConfiguration sessionServer in _sessionMcpServers)
        {
            McpServerConfiguration server = ConvertSessionServer(sessionServer);
            server.ResolveRelativePaths(workspaceRoot);
            if (!servers.TryGetValue(server.Name, out McpServerConfiguration? existing))
            {
                servers[server.Name] = server;
                continue;
            }

            existing.Merge(server);
        }

        return servers.Values
            .OrderBy(static server => server.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static McpServerConfiguration ConvertSessionServer(BackendMcpServerConfiguration source)
    {
        McpServerConfiguration server = new(source.Name)
        {
            SourcePath = string.IsNullOrWhiteSpace(source.Source)
                ? "ACP session"
                : source.Source.Trim()
        };

        CopyString(
            source,
            server,
            nameof(BackendMcpServerConfiguration.Command),
            static value => value.Command,
            static (target, value) => target.Command = value);
        CopyString(
            source,
            server,
            nameof(BackendMcpServerConfiguration.Cwd),
            static value => value.Cwd,
            static (target, value) => target.Cwd = value);
        CopyString(
            source,
            server,
            nameof(BackendMcpServerConfiguration.Url),
            static value => value.Url,
            static (target, value) => target.Url = value);
        CopyString(
            source,
            server,
            nameof(BackendMcpServerConfiguration.BearerTokenEnvVar),
            static value => value.BearerTokenEnvVar,
            static (target, value) => target.BearerTokenEnvVar = value);
        CopyString(
            source,
            server,
            nameof(BackendMcpServerConfiguration.DefaultToolsApprovalMode),
            static value => value.DefaultToolsApprovalMode,
            static (target, value) => target.DefaultToolsApprovalMode = value);

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.Args)))
        {
            server.Args.Clear();
            server.Args.AddRange(source.Args);
            server.Mark(nameof(McpServerConfiguration.Args));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.Env)))
        {
            foreach (KeyValuePair<string, string> item in source.Env)
            {
                server.Env[item.Key] = item.Value;
            }

            server.Mark(nameof(McpServerConfiguration.Env));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.EnvVars)))
        {
            server.EnvVars.Clear();
            server.EnvVars.AddRange(source.EnvVars);
            server.Mark(nameof(McpServerConfiguration.EnvVars));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.HttpHeaders)))
        {
            foreach (KeyValuePair<string, string> item in source.HttpHeaders)
            {
                server.HttpHeaders[item.Key] = item.Value;
            }

            server.Mark(nameof(McpServerConfiguration.HttpHeaders));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.EnvHttpHeaders)))
        {
            foreach (KeyValuePair<string, string> item in source.EnvHttpHeaders)
            {
                server.EnvHttpHeaders[item.Key] = item.Value;
            }

            server.Mark(nameof(McpServerConfiguration.EnvHttpHeaders));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.StartupTimeoutSeconds)))
        {
            server.StartupTimeoutSeconds = source.StartupTimeoutSeconds;
            server.Mark(nameof(McpServerConfiguration.StartupTimeoutSeconds));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.ToolTimeoutSeconds)))
        {
            server.ToolTimeoutSeconds = source.ToolTimeoutSeconds;
            server.Mark(nameof(McpServerConfiguration.ToolTimeoutSeconds));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.Enabled)))
        {
            server.Enabled = source.Enabled;
            server.Mark(nameof(McpServerConfiguration.Enabled));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.Required)))
        {
            server.Required = source.Required;
            server.Mark(nameof(McpServerConfiguration.Required));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.EnabledTools)))
        {
            server.EnabledTools.Clear();
            server.EnabledTools.AddRange(source.EnabledTools);
            server.Mark(nameof(McpServerConfiguration.EnabledTools));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.DisabledTools)))
        {
            server.DisabledTools.Clear();
            server.DisabledTools.AddRange(source.DisabledTools);
            server.Mark(nameof(McpServerConfiguration.DisabledTools));
        }

        if (source.IsAssigned(nameof(BackendMcpServerConfiguration.ToolApprovalModes)))
        {
            foreach (KeyValuePair<string, string> item in source.ToolApprovalModes)
            {
                server.ToolApprovalModes[item.Key] = item.Value;
            }

            server.Mark(nameof(McpServerConfiguration.ToolApprovalModes));
        }

        return server;
    }

    internal static bool IsAcpSource(string? source)
    {
        return source?.StartsWith(AcpSourcePrefix, StringComparison.Ordinal) == true;
    }

    private static void CopyString(
        BackendMcpServerConfiguration source,
        McpServerConfiguration target,
        string propertyName,
        Func<BackendMcpServerConfiguration, string?> getValue,
        Action<McpServerConfiguration, string?> setValue)
    {
        if (!source.IsAssigned(propertyName))
        {
            return;
        }

        setValue(target, NormalizeOptional(getValue(source)));
        target.Mark(propertyName);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
