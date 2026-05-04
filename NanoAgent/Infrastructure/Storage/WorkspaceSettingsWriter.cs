using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoAgent.Infrastructure.Storage;

internal sealed class WorkspaceSettingsWriter : IWorkspaceSettingsWriter
{
    private const string WorkspaceConfigurationDirectoryName = ".nanoagent";
    private const string WorkspaceConfigurationFileName = "agent-profile.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task SavePermissionSettingsAsync(
        string workspacePath,
        PermissionSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        string filePath = Path.Combine(
            Path.GetFullPath(workspacePath),
            WorkspaceConfigurationDirectoryName,
            WorkspaceConfigurationFileName);
        JsonObject root = await LoadRootAsync(filePath, cancellationToken);
        JsonObject application = GetOrCreateObject(root, "Application");
        JsonObject permissions = GetOrCreateObject(application, "Permissions");

        permissions["auto_approve_all_tools"] = settings.AutoApproveAllTools;
        permissions["defaultMode"] = settings.DefaultMode.ToString();
        permissions["sandboxMode"] = settings.SandboxMode.ToString();
        SetOptionalPermissionMode(permissions, "file_read", settings.FileRead);
        SetOptionalPermissionMode(permissions, "file_write", settings.FileWrite);
        SetOptionalPermissionMode(permissions, "file_delete", settings.FileDelete);
        SetOptionalPermissionMode(permissions, "shell_default", settings.ShellDefault);
        SetOptionalPermissionMode(permissions, "shell_safe", settings.ShellSafe);
        SetOptionalPermissionMode(permissions, "network", settings.Network);
        SetOptionalPermissionMode(permissions, "memory_write", settings.MemoryWrite);
        SetOptionalPermissionMode(permissions, "mcp_tools", settings.McpTools);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(
            filePath,
            root.ToJsonString(JsonOptions) + Environment.NewLine,
            cancellationToken);
    }

    private static async Task<JsonObject> LoadRootAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        string json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"{filePath} is not valid JSON.",
                exception);
        }
    }

    private static JsonObject GetOrCreateObject(
        JsonObject parent,
        string propertyName)
    {
        if (parent[propertyName] is JsonObject existing)
        {
            return existing;
        }

        JsonObject created = [];
        parent[propertyName] = created;
        return created;
    }

    private static void SetOptionalPermissionMode(
        JsonObject permissions,
        string propertyName,
        PermissionMode? mode)
    {
        if (mode is not null)
        {
            permissions[propertyName] = mode.Value.ToString();
        }
    }
}
