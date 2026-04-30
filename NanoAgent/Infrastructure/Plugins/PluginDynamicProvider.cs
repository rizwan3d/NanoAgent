using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace NanoAgent.Infrastructure.Plugins;

internal sealed class PluginDynamicProvider : IDynamicToolProvider
{
    private readonly IEnumerable<IPluginToolFactory> _factories;
    private readonly ILogger<PluginDynamicProvider> _logger;
    private readonly IUserDataPathProvider _userDataPathProvider;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;
    private readonly object _gate = new();
    private bool _initialized;
    private IReadOnlyList<ITool> _tools = [];
    private IReadOnlyList<DynamicToolProviderStatus> _statuses = [];

    public PluginDynamicProvider(
        IEnumerable<IPluginToolFactory> factories,
        IUserDataPathProvider userDataPathProvider,
        IWorkspaceRootProvider workspaceRootProvider,
        ILogger<PluginDynamicProvider> logger)
    {
        _factories = factories;
        _userDataPathProvider = userDataPathProvider;
        _workspaceRootProvider = workspaceRootProvider;
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

            Initialize();
            _initialized = true;
        }
    }

    private void Initialize()
    {
        Dictionary<string, PluginConfiguration> configurations = AgentProfileConfigurationReader
            .LoadPlugins(_userDataPathProvider, _workspaceRootProvider)
            .ToDictionary(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IPluginToolFactory> factories = _factories
            .Where(static factory => !string.IsNullOrWhiteSpace(factory.PluginName))
            .ToDictionary(static factory => factory.PluginName, StringComparer.OrdinalIgnoreCase);

        foreach (IPluginToolFactory factory in factories.Values)
        {
            configurations.TryAdd(factory.PluginName, new PluginConfiguration(factory.PluginName));
        }

        List<ITool> tools = [];
        List<DynamicToolProviderStatus> statuses = [];
        HashSet<string> usedToolNames = new(StringComparer.Ordinal);

        foreach (PluginConfiguration configuration in configurations.Values.OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!configuration.Enabled)
            {
                statuses.Add(CreateStatus(configuration, enabled: false, available: false, toolCount: 0, "disabled"));
                continue;
            }

            if (!factories.TryGetValue(configuration.Name, out IPluginToolFactory? factory))
            {
                statuses.Add(CreateStatus(configuration, enabled: true, available: false, toolCount: 0, "plugin is not installed"));
                continue;
            }

            try
            {
                IReadOnlyList<ITool> pluginTools = factory.CreateTools(configuration);
                foreach (ITool tool in pluginTools)
                {
                    if (!usedToolNames.Add(tool.Name))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate plugin tool registration detected for '{tool.Name}'.");
                    }

                    tools.Add(tool);
                }

                statuses.Add(CreateStatus(configuration, enabled: true, available: true, pluginTools.Count, "registered"));
            }
            catch (Exception exception)
            {
                string message = $"Plugin '{configuration.Name}' unavailable: {exception.Message}";
                _logger.LogWarning(exception, "{Message}", message);
                statuses.Add(CreateStatus(configuration, enabled: true, available: false, toolCount: 0, exception.Message));

                if (configuration.Required)
                {
                    throw new InvalidOperationException(message, exception);
                }
            }
        }

        _tools = tools;
        _statuses = statuses;
    }

    private static DynamicToolProviderStatus CreateStatus(
        PluginConfiguration configuration,
        bool enabled,
        bool available,
        int toolCount,
        string? details)
    {
        return new DynamicToolProviderStatus(
            configuration.Name,
            "plugin",
            enabled,
            available,
            toolCount,
            details);
    }
}
