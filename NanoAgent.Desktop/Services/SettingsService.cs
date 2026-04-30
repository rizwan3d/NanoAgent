using NanoAgent.Desktop.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoAgent.Desktop.Services;

public class SettingsService
{
    private const string ApplicationDirectoryName = "NanoAgent";
    private const string ProfileFileName = "agent-profile.json";
    private const string DesktopPropertyName = "desktop";
    private const string WorkspacesPropertyName = "workspaces";
    private const string LegacySettingsDirectoryName = "NanoAgent.Desktop";
    private const string LegacySettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _profileDirectoryPath;
    private readonly string _profilePath;
    private readonly string _legacySettingsPath;

    public SettingsService()
    {
        _profileDirectoryPath = Path.Combine(
            ResolveFolder(Environment.SpecialFolder.ApplicationData, ".config"),
            ApplicationDirectoryName);
        Directory.CreateDirectory(_profileDirectoryPath);

        _profilePath = Path.Combine(_profileDirectoryPath, ProfileFileName);

        var legacySettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacySettingsDirectoryName);
        _legacySettingsPath = Path.Combine(legacySettingsDirectory, LegacySettingsFileName);
    }

    public IReadOnlyList<ProjectInfo> LoadRecentProjects()
    {
        if (TryLoadRecentProjectsFromProfile(out IReadOnlyList<ProjectInfo> projects))
        {
            return projects;
        }

        return LoadLegacyRecentProjects();
    }

    public async Task SaveRecentProjectsAsync(IEnumerable<ProjectInfo> projects)
    {
        var projectList = projects.ToList();
        JsonObject root = await LoadProfileRootForWriteAsync();

        if (root[DesktopPropertyName] is not JsonObject desktop)
        {
            desktop = new JsonObject();
            root[DesktopPropertyName] = desktop;
        }

        desktop[WorkspacesPropertyName] = JsonSerializer.SerializeToNode(projectList, SerializerOptions);

        var json = root.ToJsonString(SerializerOptions);
        await File.WriteAllTextAsync(_profilePath, json);
    }

    private bool TryLoadRecentProjectsFromProfile(out IReadOnlyList<ProjectInfo> projects)
    {
        projects = [];

        if (!File.Exists(_profilePath))
        {
            return false;
        }

        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(_profilePath));
            JsonNode? workspaces = root?[DesktopPropertyName]?[WorkspacesPropertyName];
            if (workspaces is null)
            {
                return false;
            }

            projects = workspaces.Deserialize<List<ProjectInfo>>(SerializerOptions) ?? [];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private IReadOnlyList<ProjectInfo> LoadLegacyRecentProjects()
    {
        if (!File.Exists(_legacySettingsPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_legacySettingsPath);
            return JsonSerializer.Deserialize<List<ProjectInfo>>(json, SerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<JsonObject> LoadProfileRootForWriteAsync()
    {
        if (!File.Exists(_profilePath))
        {
            return new JsonObject();
        }

        var json = await File.ReadAllTextAsync(_profilePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        JsonNode? root = JsonNode.Parse(json);
        if (root is JsonObject rootObject)
        {
            return rootObject;
        }

        throw new InvalidOperationException($"Cannot save desktop workspaces because '{_profilePath}' does not contain a JSON object.");
    }

    private static string ResolveFolder(Environment.SpecialFolder specialFolder, string fallbackRelativePath)
    {
        string folderPath = Environment.GetFolderPath(specialFolder);
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            return folderPath;
        }

        string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfilePath))
        {
            throw new InvalidOperationException($"Unable to resolve storage path for '{specialFolder}'.");
        }

        return Path.Combine(userProfilePath, fallbackRelativePath);
    }
}
