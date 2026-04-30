using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Mcp;

internal sealed class McpDynamicToolProvider : IDynamicToolProvider, IAsyncDisposable
{
    private readonly NanoAgentMcpConfigLoader _configLoader;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpDynamicToolProvider> _logger;
    private readonly object _gate = new();
    private bool _initialized;
    private IReadOnlyList<ITool> _tools = [];
    private IReadOnlyList<DynamicToolProviderStatus> _statuses = [];
    private IReadOnlyList<IMcpServerClient> _clients = [];

    public McpDynamicToolProvider(
        NanoAgentMcpConfigLoader configLoader,
        IHttpClientFactory httpClientFactory,
        ILogger<McpDynamicToolProvider> logger)
    {
        _configLoader = configLoader;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IReadOnlyList<ITool> GetTools()
    {
        EnsureInitialized();
        return _tools;
    }

    public IReadOnlyList<DynamicToolProviderStatus> GetStatuses()
    {
        EnsureInitialized();
        return _statuses;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IMcpServerClient client in _clients)
        {
            await client.DisposeAsync();
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            InitializeAsync().GetAwaiter().GetResult();
            _initialized = true;
        }
    }

    private async Task InitializeAsync()
    {
        IReadOnlyList<McpServerConfiguration> configurations = _configLoader.Load();
        List<ITool> tools = [];
        List<DynamicToolProviderStatus> statuses = [];
        List<IMcpServerClient> clients = [];
        HashSet<string> usedToolNames = new(StringComparer.Ordinal);

        foreach (McpServerConfiguration configuration in configurations)
        {
            if (!configuration.Enabled)
            {
                statuses.Add(new DynamicToolProviderStatus(
                    configuration.Name,
                    GetTransportKind(configuration),
                    Enabled: false,
                    IsAvailable: false,
                    ToolCount: 0,
                    Details: "disabled"));
                continue;
            }

            IMcpServerClient? client = null;
            try
            {
                client = CreateClient(configuration);
                using CancellationTokenSource startupTimeout = new(
                    TimeSpan.FromSeconds(configuration.StartupTimeoutSeconds));
                await client.InitializeAsync(startupTimeout.Token);
                IReadOnlyList<McpRemoteTool> remoteTools = await client.ListToolsAsync(startupTimeout.Token);
                McpRemoteTool[] enabledTools = remoteTools
                    .Where(tool => configuration.ShouldIncludeTool(tool.Name))
                    .ToArray();

                foreach (McpRemoteTool remoteTool in enabledTools)
                {
                    string toolName = McpToolName.Create(
                        configuration.Name,
                        remoteTool.Name,
                        usedToolNames);
                    usedToolNames.Add(toolName);
                    tools.Add(new McpTool(
                        toolName,
                        remoteTool.Name,
                        remoteTool.Description,
                        remoteTool.InputSchema,
                        client,
                        GetApprovalMode(configuration, remoteTool.Name),
                        TimeSpan.FromSeconds(configuration.ToolTimeoutSeconds)));
                }

                clients.Add(client);
                statuses.Add(new DynamicToolProviderStatus(
                    configuration.Name,
                    client.TransportKind,
                    Enabled: true,
                    IsAvailable: true,
                    ToolCount: enabledTools.Length,
                    Details: client.Endpoint));
            }
            catch (Exception exception)
            {
                if (client is not null)
                {
                    await client.DisposeAsync();
                }

                string message = $"MCP server '{configuration.Name}' unavailable: {exception.Message}";
                _logger.LogWarning(exception, "{Message}", message);
                statuses.Add(new DynamicToolProviderStatus(
                    configuration.Name,
                    GetTransportKind(configuration),
                    Enabled: true,
                    IsAvailable: false,
                    ToolCount: 0,
                    Details: exception.Message));

                if (configuration.Required)
                {
                    throw new InvalidOperationException(message, exception);
                }
            }
        }

        _tools = tools;
        _statuses = statuses;
        _clients = clients;
    }

    private IMcpServerClient CreateClient(McpServerConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.Command))
        {
            return new McpStdioServerClient(configuration);
        }

        if (!string.IsNullOrWhiteSpace(configuration.Url))
        {
            return new McpHttpServerClient(
                _httpClientFactory.CreateClient("NanoAgent.Mcp"),
                configuration);
        }

        throw new InvalidOperationException(
            "Configure either 'command' for stdio MCP or 'url' for streamable HTTP MCP.");
    }

    private static string GetTransportKind(McpServerConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.Command))
        {
            return "stdio";
        }

        return !string.IsNullOrWhiteSpace(configuration.Url)
            ? "http"
            : "unknown";
    }

    private static ToolApprovalMode GetApprovalMode(
        McpServerConfiguration configuration,
        string remoteToolName)
    {
        string? configuredMode = configuration.ToolApprovalModes.TryGetValue(remoteToolName, out string? toolMode)
            ? toolMode
            : configuration.DefaultToolsApprovalMode;

        return configuredMode?.Trim().ToLowerInvariant() switch
        {
            "approve" or "auto" => ToolApprovalMode.Automatic,
            "prompt" => ToolApprovalMode.RequireApproval,
            _ => ToolApprovalMode.RequireApproval
        };
    }
}
