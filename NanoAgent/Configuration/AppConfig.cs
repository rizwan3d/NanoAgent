namespace NanoAgent;

internal sealed record AppConfig(
    string AppName,
    string Endpoint,
    string Model,
    string ApiKey,
    int MaxSessionMessages,
    int MaxSessionEstimatedTokens)
{
    public static AppConfig CreateDefault() =>
        new(
            AppName: "NanoAgent",
            Endpoint: "http://127.0.0.1:1234/v1",
            Model: "google/gemma-4-e4b",
            ApiKey: string.Empty,
            MaxSessionMessages: 48,
            MaxSessionEstimatedTokens: 24000);

    public AppConfig Merge(AppConfigFile? overrideConfig) =>
        new(
            AppName: string.IsNullOrWhiteSpace(overrideConfig?.AppName) ? AppName : overrideConfig.AppName,
            Endpoint: string.IsNullOrWhiteSpace(overrideConfig?.Endpoint) ? Endpoint : overrideConfig.Endpoint,
            Model: string.IsNullOrWhiteSpace(overrideConfig?.Model) ? Model : overrideConfig.Model,
            ApiKey: string.IsNullOrWhiteSpace(overrideConfig?.ApiKey) ? ApiKey : overrideConfig.ApiKey,
            MaxSessionMessages: overrideConfig?.MaxSessionMessages is > 0 ? overrideConfig.MaxSessionMessages.Value : MaxSessionMessages,
            MaxSessionEstimatedTokens: overrideConfig?.MaxSessionEstimatedTokens is > 0 ? overrideConfig.MaxSessionEstimatedTokens.Value : MaxSessionEstimatedTokens);

    public void Validate()
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(AppName))
        {
            errors.Add("'appName' is required.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            errors.Add("'model' is required.");
        }

        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            errors.Add("'endpoint' is required.");
        }
        else if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri? endpointUri))
        {
            errors.Add("'endpoint' must be a valid absolute URI.");
        }
        else
        {
            if (endpointUri.Scheme is not ("http" or "https"))
            {
                errors.Add("'endpoint' must use http or https.");
            }

            if (string.IsNullOrWhiteSpace(endpointUri.Host))
            {
                errors.Add("'endpoint' must include a host.");
            }
        }

        if (MaxSessionMessages < 4)
        {
            errors.Add("'maxSessionMessages' must be at least 4.");
        }

        if (MaxSessionEstimatedTokens < 1024)
        {
            errors.Add("'maxSessionEstimatedTokens' must be at least 1024.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid NanoAgent configuration: " + string.Join(" ", errors));
        }
    }

    public AppConfigFile ToFileModel() =>
        new()
        {
            AppName = AppName,
            Endpoint = Endpoint,
            Model = Model,
            ApiKey = ApiKey,
            MaxSessionMessages = MaxSessionMessages,
            MaxSessionEstimatedTokens = MaxSessionEstimatedTokens
        };
}

internal sealed class AppConfigFile
{
    public string? AppName { get; init; }

    public string? Endpoint { get; init; }

    public string? Model { get; init; }

    public string? ApiKey { get; init; }

    public int? MaxSessionMessages { get; init; }

    public int? MaxSessionEstimatedTokens { get; init; }
}
