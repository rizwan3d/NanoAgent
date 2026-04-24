using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NanoAgent.Desktop.Models;

namespace NanoAgent.Desktop.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NanoAgent.Desktop");
        Directory.CreateDirectory(settingsDirectory);

        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    public IReadOnlyList<ProjectInfo> LoadRecentProjects()
    {
        if (!File.Exists(_settingsPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<List<ProjectInfo>>(json, SerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveRecentProjectsAsync(IEnumerable<ProjectInfo> projects)
    {
        var json = JsonSerializer.Serialize(projects, SerializerOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
    }
}
