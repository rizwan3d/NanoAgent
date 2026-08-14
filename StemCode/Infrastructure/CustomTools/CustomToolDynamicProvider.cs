using Microsoft.Extensions.Logging;
using StemCode.Application.Abstractions;
using StemCode.Infrastructure.Storage;
using System.Text.Json;

namespace StemCode.Infrastructure.CustomTools;

internal sealed class CustomToolDynamicProvider : IDynamicToolProvider
{
    private readonly CustomToolProcessExecutor _processExecutor;
    private readonly IUserDataPathProvider _userDataPathProvider;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;
    private readonly ILogger<CustomToolDynamicProvider> _logger;
    private readonly object _gate = new();
    private bool _initialized;
    private IReadOnlyList<ITool> _tools = [];
    private IReadOnlyList<DynamicToolProviderStatus> _statuses = [];

    public CustomToolDynamicProvider(
        IUserDataPathProvider userDataPathProvider,
        IWorkspaceRootProvider workspaceRootProvider,
        CustomToolProcessExecutor processExecutor,
        ILogger<CustomToolDynamicProvider> logger)
    {
        _userDataPathProvider = userDataPathProvider;
        _workspaceRootProvider = workspaceRootProvider;
        _processExecutor = processExecutor;
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
        IReadOnlyList<CustomToolConfiguration> configurations = AgentProfileConfigurationReader.LoadCustomTools(
            _userDataPathProvider,
            _workspaceRootProvider);
        List<ITool> tools = [];
        List<DynamicToolProviderStatus> statuses = [];
        HashSet<string> usedToolNames = new(StringComparer.Ordinal);
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());

        foreach (CustomToolConfiguration configuration in configurations)
        {
            configuration.UntrustedWorkspaceDefinition = IsWorkspaceDefined(configuration.SourcePath, workspaceRoot);

            if (!configuration.Enabled)
            {
                statuses.Add(CreateStatus(configuration, enabled: false, available: false, toolCount: 0, "disabled"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(configuration.Command))
            {
                statuses.Add(CreateStatus(configuration, enabled: true, available: false, toolCount: 0, "missing command"));
                continue;
            }

            if (!TryValidateSchema(configuration, out string? schemaError))
            {
                statuses.Add(CreateStatus(configuration, enabled: true, available: false, toolCount: 0, schemaError));
                continue;
            }

            try
            {
                string toolName = CustomToolName.Create(configuration.Name, usedToolNames);
                usedToolNames.Add(toolName);
                tools.Add(new CustomTool(toolName, configuration, _processExecutor));
                statuses.Add(CreateStatus(configuration, enabled: true, available: true, toolCount: 1, configuration.Command));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Custom tool '{ToolName}' unavailable: {Message}", configuration.Name, exception.Message);
                statuses.Add(CreateStatus(configuration, enabled: true, available: false, toolCount: 0, exception.Message));
            }
        }

        _tools = tools;
        _statuses = statuses;
    }

    private static bool TryValidateSchema(
        CustomToolConfiguration configuration,
        out string? error)
    {
        error = null;
        string schema = configuration.GetSchema();
        try
        {
            using JsonDocument document = JsonDocument.Parse(schema);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }
        catch (JsonException exception)
        {
            error = $"invalid schema: {exception.Message}";
            return false;
        }

        error = "schema must be a JSON object";
        return false;
    }

    private static DynamicToolProviderStatus CreateStatus(
        CustomToolConfiguration configuration,
        bool enabled,
        bool available,
        int toolCount,
        string? details)
    {
        return new DynamicToolProviderStatus(
            configuration.Name,
            "custom",
            enabled,
            available,
            toolCount,
            details);
    }

    private static bool IsWorkspaceDefined(
        string? sourcePath,
        string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        string normalizedSourcePath = Path.GetFullPath(sourcePath);
        string normalizedWorkspaceRoot = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string workspacePrefix = normalizedWorkspaceRoot + Path.DirectorySeparatorChar;

        return string.Equals(normalizedSourcePath, normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedSourcePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase);
    }
}
