using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.CustomTools;
using NanoAgent.Infrastructure.Mcp;

namespace NanoAgent.Infrastructure.Storage;

internal static class AgentProfileConfigurationReader
{
    private const string WorkspaceConfigurationDirectoryName = ".nanoagent";
    private const string WorkspaceConfigurationFileName = "agent-profile.json";

    public static MemorySettings LoadMemorySettings(
        IUserDataPathProvider userDataPathProvider,
        IWorkspaceRootProvider workspaceRootProvider)
    {
        ArgumentNullException.ThrowIfNull(userDataPathProvider);
        ArgumentNullException.ThrowIfNull(workspaceRootProvider);

        MemorySettings settings = new();
        foreach (AgentProfileConfigurationDocument document in LoadDocuments(
                     userDataPathProvider,
                     workspaceRootProvider))
        {
            if (document.Memory is not null)
            {
                MergeMemorySettings(settings, document.Memory);
            }
        }

        return NormalizeMemorySettings(settings);
    }

    public static ToolAuditSettings LoadToolAuditSettings(
        IUserDataPathProvider userDataPathProvider,
        IWorkspaceRootProvider workspaceRootProvider)
    {
        ArgumentNullException.ThrowIfNull(userDataPathProvider);
        ArgumentNullException.ThrowIfNull(workspaceRootProvider);

        ToolAuditSettings settings = new();
        foreach (AgentProfileConfigurationDocument document in LoadDocuments(
                     userDataPathProvider,
                     workspaceRootProvider))
        {
            if (document.ToolAudit is not null)
            {
                MergeToolAuditSettings(settings, document.ToolAudit);
            }

            if (document.ToolAuditLog is not null)
            {
                MergeToolAuditSettings(settings, document.ToolAuditLog);
            }
        }

        return NormalizeToolAuditSettings(settings);
    }

    public static IReadOnlyList<McpServerConfiguration> LoadMcpServers(
        IUserDataPathProvider userDataPathProvider,
        IWorkspaceRootProvider workspaceRootProvider)
    {
        ArgumentNullException.ThrowIfNull(userDataPathProvider);
        ArgumentNullException.ThrowIfNull(workspaceRootProvider);

        string workspaceRoot = Path.GetFullPath(workspaceRootProvider.GetWorkspaceRoot());
        Dictionary<string, McpServerConfiguration> servers = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string path, AgentProfileConfigurationDocument document) in LoadDocumentPairs(
                     userDataPathProvider,
                     workspaceRootProvider))
        {
            foreach (KeyValuePair<string, McpServerProfileDocument> item in document.McpServers ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Key) || item.Value is null)
                {
                    continue;
                }

                McpServerConfiguration server = ConvertServer(item.Key, item.Value, path);
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

    public static IReadOnlyList<CustomToolConfiguration> LoadCustomTools(
        IUserDataPathProvider userDataPathProvider,
        IWorkspaceRootProvider workspaceRootProvider)
    {
        ArgumentNullException.ThrowIfNull(userDataPathProvider);
        ArgumentNullException.ThrowIfNull(workspaceRootProvider);

        string workspaceRoot = Path.GetFullPath(workspaceRootProvider.GetWorkspaceRoot());
        Dictionary<string, CustomToolConfiguration> tools = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string path, AgentProfileConfigurationDocument document) in LoadDocumentPairs(
                     userDataPathProvider,
                     workspaceRootProvider))
        {
            foreach (KeyValuePair<string, CustomToolProfileDocument> item in document.CustomTools ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Key) || item.Value is null)
                {
                    continue;
                }

                CustomToolConfiguration configuration = ConvertCustomTool(item.Key, item.Value, path);
                configuration.ResolveRelativePaths(workspaceRoot);
                tools[configuration.Name] = configuration;
            }
        }

        return tools.Values
            .OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string GetWorkspaceConfigurationFilePath(string workspaceRoot)
    {
        return WorkspacePath.Resolve(
            Path.GetFullPath(workspaceRoot),
            Path.Combine(WorkspaceConfigurationDirectoryName, WorkspaceConfigurationFileName));
    }

    public static async Task<AgentProfileConfigurationDocument?> LoadUserDocumentAsync(
        IUserDataPathProvider userDataPathProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userDataPathProvider);
        return await LoadDocumentAsync(
            userDataPathProvider.GetConfigurationFilePath(),
            cancellationToken);
    }

    public static async Task SaveUserDocumentAsync(
        IUserDataPathProvider userDataPathProvider,
        AgentProfileConfigurationDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userDataPathProvider);
        ArgumentNullException.ThrowIfNull(document);

        string filePath = userDataPathProvider.GetConfigurationFilePath();
        string directoryPath = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Configuration path does not contain a parent directory.");

        FilePermissionHelper.EnsurePrivateDirectory(directoryPath);

        await using FileStream stream = new(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await JsonSerializer.SerializeAsync(
            stream,
            document,
            OnboardingStorageJsonContext.Default.AgentProfileConfigurationDocument,
            cancellationToken);

        await stream.FlushAsync(cancellationToken);
        FilePermissionHelper.EnsurePrivateFile(filePath);
    }

    private static IEnumerable<AgentProfileConfigurationDocument> LoadDocuments(
        IUserDataPathProvider userDataPathProvider,
        IWorkspaceRootProvider workspaceRootProvider)
    {
        return LoadDocumentPairs(userDataPathProvider, workspaceRootProvider)
            .Select(static pair => pair.Document);
    }

    private static IEnumerable<(string Path, AgentProfileConfigurationDocument Document)> LoadDocumentPairs(
        IUserDataPathProvider userDataPathProvider,
        IWorkspaceRootProvider workspaceRootProvider)
    {
        foreach (string path in GetConfigurationPaths(userDataPathProvider, workspaceRootProvider))
        {
            AgentProfileConfigurationDocument? document = LoadDocument(path);
            if (document is not null)
            {
                yield return (path, document);
            }
        }
    }

    private static IReadOnlyList<string> GetConfigurationPaths(
        IUserDataPathProvider userDataPathProvider,
        IWorkspaceRootProvider workspaceRootProvider)
    {
        string workspaceProfilePath = GetWorkspaceConfigurationFilePath(
            workspaceRootProvider.GetWorkspaceRoot());
        string userProfilePath = Path.GetFullPath(userDataPathProvider.GetConfigurationFilePath());

        return userProfilePath.Equals(workspaceProfilePath, StringComparison.OrdinalIgnoreCase)
            ? [userProfilePath]
            : [userProfilePath, workspaceProfilePath];
    }

    private static AgentProfileConfigurationDocument? LoadDocument(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                json,
                OnboardingStorageJsonContext.Default.AgentProfileConfigurationDocument);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<AgentProfileConfigurationDocument?> LoadDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                json,
                OnboardingStorageJsonContext.Default.AgentProfileConfigurationDocument);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static McpServerConfiguration ConvertServer(
        string name,
        McpServerProfileDocument document,
        string sourcePath)
    {
        McpServerConfiguration server = new(name)
        {
            SourcePath = sourcePath
        };

        if (document.Command is not null)
        {
            server.Command = NormalizeOptional(document.Command);
            server.Mark(nameof(McpServerConfiguration.Command));
        }

        if (document.Args is not null)
        {
            server.Args.Clear();
            server.Args.AddRange(NormalizeStringList(document.Args));
            server.Mark(nameof(McpServerConfiguration.Args));
        }

        if (document.Env is not null)
        {
            server.Env.Clear();
            foreach (KeyValuePair<string, string> item in NormalizeDictionary(document.Env))
            {
                server.Env[item.Key] = item.Value;
            }

            server.Mark(nameof(McpServerConfiguration.Env));
        }

        if (document.EnvVars is not null)
        {
            server.EnvVars.Clear();
            server.EnvVars.AddRange(NormalizeStringList(document.EnvVars));
            server.Mark(nameof(McpServerConfiguration.EnvVars));
        }

        if (document.Cwd is not null)
        {
            server.Cwd = NormalizeOptional(document.Cwd);
            server.Mark(nameof(McpServerConfiguration.Cwd));
        }

        if (document.Url is not null)
        {
            server.Url = NormalizeOptional(document.Url);
            server.Mark(nameof(McpServerConfiguration.Url));
        }

        if (document.BearerTokenEnvVar is not null)
        {
            server.BearerTokenEnvVar = NormalizeOptional(document.BearerTokenEnvVar);
            server.Mark(nameof(McpServerConfiguration.BearerTokenEnvVar));
        }

        if (document.HttpHeaders is not null)
        {
            server.HttpHeaders.Clear();
            foreach (KeyValuePair<string, string> item in NormalizeDictionary(document.HttpHeaders))
            {
                server.HttpHeaders[item.Key] = item.Value;
            }

            server.Mark(nameof(McpServerConfiguration.HttpHeaders));
        }

        if (document.EnvHttpHeaders is not null)
        {
            server.EnvHttpHeaders.Clear();
            foreach (KeyValuePair<string, string> item in NormalizeDictionary(document.EnvHttpHeaders))
            {
                server.EnvHttpHeaders[item.Key] = item.Value;
            }

            server.Mark(nameof(McpServerConfiguration.EnvHttpHeaders));
        }

        if (document.StartupTimeoutSeconds is > 0)
        {
            server.StartupTimeoutSeconds = document.StartupTimeoutSeconds.Value;
            server.Mark(nameof(McpServerConfiguration.StartupTimeoutSeconds));
        }

        if (document.ToolTimeoutSeconds is > 0)
        {
            server.ToolTimeoutSeconds = document.ToolTimeoutSeconds.Value;
            server.Mark(nameof(McpServerConfiguration.ToolTimeoutSeconds));
        }

        if (document.Enabled is not null)
        {
            server.Enabled = document.Enabled.Value;
            server.Mark(nameof(McpServerConfiguration.Enabled));
        }

        if (document.Required is not null)
        {
            server.Required = document.Required.Value;
            server.Mark(nameof(McpServerConfiguration.Required));
        }

        if (document.EnabledTools is not null)
        {
            server.EnabledTools.Clear();
            server.EnabledTools.AddRange(NormalizeStringList(document.EnabledTools));
            server.Mark(nameof(McpServerConfiguration.EnabledTools));
        }

        if (document.DisabledTools is not null)
        {
            server.DisabledTools.Clear();
            server.DisabledTools.AddRange(NormalizeStringList(document.DisabledTools));
            server.Mark(nameof(McpServerConfiguration.DisabledTools));
        }

        if (document.DefaultToolsApprovalMode is not null)
        {
            server.DefaultToolsApprovalMode = NormalizeOptional(document.DefaultToolsApprovalMode);
            server.Mark(nameof(McpServerConfiguration.DefaultToolsApprovalMode));
        }

        MergeToolApprovalModes(server, document);
        return server;
    }

    private static CustomToolConfiguration ConvertCustomTool(
        string name,
        CustomToolProfileDocument document,
        string sourcePath)
    {
        CustomToolConfiguration tool = new(name)
        {
            ApprovalMode = NormalizeOptional(document.ApprovalMode),
            Command = NormalizeOptional(document.Command),
            Cwd = NormalizeOptional(document.Cwd),
            Description = NormalizeOptional(document.Description),
            SourcePath = sourcePath
        };

        if (document.Args is not null)
        {
            tool.Args.Clear();
            tool.Args.AddRange(NormalizeStringList(document.Args));
        }

        if (document.Env is not null)
        {
            tool.Env.Clear();
            foreach (KeyValuePair<string, string> item in NormalizeDictionary(document.Env))
            {
                tool.Env[item.Key] = item.Value;
            }
        }

        if (document.Enabled is not null)
        {
            tool.Enabled = document.Enabled.Value;
        }

        if (document.MaxOutputChars is > 0)
        {
            tool.MaxOutputChars = Math.Min(document.MaxOutputChars.Value, 250_000);
        }

        if (document.TimeoutSeconds is > 0)
        {
            tool.TimeoutSeconds = Math.Min(document.TimeoutSeconds.Value, 600);
        }

        if (document.Schema is { } schema)
        {
            tool.Schema = schema.Clone();
        }

        return tool;
    }

    private static void MergeToolApprovalModes(
        McpServerConfiguration server,
        McpServerProfileDocument document)
    {
        bool assigned = false;
        if (document.ToolApprovalModes is not null)
        {
            foreach (KeyValuePair<string, string> item in NormalizeDictionary(document.ToolApprovalModes))
            {
                server.ToolApprovalModes[item.Key] = item.Value;
                assigned = true;
            }
        }

        foreach (KeyValuePair<string, McpToolProfileDocument> item in document.Tools ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Key) ||
                item.Value is null ||
                string.IsNullOrWhiteSpace(item.Value.ApprovalMode))
            {
                continue;
            }

            server.ToolApprovalModes[item.Key.Trim()] = item.Value.ApprovalMode.Trim();
            assigned = true;
        }

        if (assigned)
        {
            server.Mark(nameof(McpServerConfiguration.ToolApprovalModes));
        }
    }

    private static void MergeMemorySettings(
        MemorySettings target,
        MemoryProfileDocument source)
    {
        if (source.AllowAutoFailureObservation is not null)
        {
            target.AllowAutoFailureObservation = source.AllowAutoFailureObservation.Value;
        }

        if (source.AllowAutoManualLessons is not null)
        {
            target.AllowAutoManualLessons = source.AllowAutoManualLessons.Value;
        }

        if (source.Disabled is not null)
        {
            target.Disabled = source.Disabled.Value;
        }

        if (source.MaxEntries is not null)
        {
            target.MaxEntries = source.MaxEntries.Value;
        }

        if (source.MaxPromptChars is not null)
        {
            target.MaxPromptChars = source.MaxPromptChars.Value;
        }

        if (source.RedactSecrets is not null)
        {
            target.RedactSecrets = source.RedactSecrets.Value;
        }

        if (source.RequireApprovalForWrites is not null)
        {
            target.RequireApprovalForWrites = source.RequireApprovalForWrites.Value;
        }
    }

    private static void MergeToolAuditSettings(
        ToolAuditSettings target,
        ToolAuditProfileDocument source)
    {
        if (source.Enabled is not null)
        {
            target.Enabled = source.Enabled.Value;
        }

        if (source.MaxArgumentsChars is not null)
        {
            target.MaxArgumentsChars = source.MaxArgumentsChars.Value;
        }

        if (source.MaxResultChars is not null)
        {
            target.MaxResultChars = source.MaxResultChars.Value;
        }

        if (source.RedactSecrets is not null)
        {
            target.RedactSecrets = source.RedactSecrets.Value;
        }
    }

    private static MemorySettings NormalizeMemorySettings(MemorySettings settings)
    {
        settings.MaxEntries = settings.MaxEntries <= 0
            ? 500
            : Math.Min(settings.MaxEntries, 10_000);
        settings.MaxPromptChars = settings.MaxPromptChars <= 0
            ? 12_000
            : Math.Min(settings.MaxPromptChars, 100_000);
        return settings;
    }

    private static ToolAuditSettings NormalizeToolAuditSettings(ToolAuditSettings settings)
    {
        settings.MaxArgumentsChars = settings.MaxArgumentsChars <= 0
            ? 12_000
            : Math.Min(settings.MaxArgumentsChars, 250_000);
        settings.MaxResultChars = settings.MaxResultChars <= 0
            ? 12_000
            : Math.Min(settings.MaxResultChars, 250_000);
        return settings;
    }

    private static IReadOnlyList<string> NormalizeStringList(IEnumerable<string> values)
    {
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> NormalizeDictionary(
        IReadOnlyDictionary<string, string> values)
    {
        return values
            .Where(static item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(
                static item => item.Key.Trim(),
                static item => item.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
