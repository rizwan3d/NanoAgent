using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoAgent;

internal static class AppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string GetGlobalConfigPath()
    {
        string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDirectory, "NanoAgent", "config.json");
    }

    public static string GetLocalOverridePath() =>
        Path.Combine(Environment.CurrentDirectory, ".NanoAgent", "config.json");

    public static AppConfig Load()
    {
        AppConfig defaultConfig = AppConfig.CreateDefault();
        string globalConfigPath = GetGlobalConfigPath();
        EnsureConfigFileExists(globalConfigPath, defaultConfig);

        AppConfig config = defaultConfig.Merge(ReadConfigFile(globalConfigPath));
        string localOverridePath = GetLocalOverridePath();

        if (File.Exists(localOverridePath))
        {
            config = config.Merge(ReadConfigFile(localOverridePath));
        }

        return config;
    }

    public static void EditGlobalConfig()
    {
        string globalConfigPath = GetGlobalConfigPath();
        EnsureConfigFileExists(globalConfigPath, AppConfig.CreateDefault());

        Process.Start(new ProcessStartInfo
        {
            FileName = globalConfigPath,
            UseShellExecute = true
        });
    }

    private static void EnsureConfigFileExists(string configPath, AppConfig defaultConfig)
    {
        if (File.Exists(configPath))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(defaultConfig.ToFileModel(), AppConfigJsonContext.Default.AppConfigFile);
        File.WriteAllText(configPath, json + Environment.NewLine);
    }

    private static AppConfigFile? ReadConfigFile(string configPath)
    {
        string json = File.ReadAllText(configPath);

        try
        {
            return JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfigFile);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Invalid JSON in config file: {configPath}", exception);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AppConfigFile))]
internal sealed partial class AppConfigJsonContext : JsonSerializerContext
{
}
