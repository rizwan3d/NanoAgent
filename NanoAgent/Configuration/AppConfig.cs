namespace NanoAgent;

internal sealed record AppConfig(string AppName, string Endpoint, string Model)
{
    public static AppConfig CreateDefault() =>
        new(
            AppName: "NanoAgent",
            Endpoint: "http://127.0.0.1:1234/v1",
            Model: "google/gemma-4-e4b");

    public AppConfig Merge(AppConfigFile? overrideConfig) =>
        new(
            AppName: string.IsNullOrWhiteSpace(overrideConfig?.AppName) ? AppName : overrideConfig.AppName,
            Endpoint: string.IsNullOrWhiteSpace(overrideConfig?.Endpoint) ? Endpoint : overrideConfig.Endpoint,
            Model: string.IsNullOrWhiteSpace(overrideConfig?.Model) ? Model : overrideConfig.Model);

    public AppConfigFile ToFileModel() =>
        new()
        {
            AppName = AppName,
            Endpoint = Endpoint,
            Model = Model
        };
}

internal sealed class AppConfigFile
{
    public string? AppName { get; init; }

    public string? Endpoint { get; init; }

    public string? Model { get; init; }
}
