namespace NanoAgent.Application.Models;

public sealed record BudgetControlsSettings(
    string Source,
    string? CloudApiUrl,
    string? LocalPath,
    bool HasCloudAuthKey,
    DateTimeOffset UpdatedAtUtc)
{
    public const string CloudSource = "Cloud";
    public const string DefaultLocalPath = ".nanoagent/budget-controls.local.json";
    public const string LocalSource = "Local";

    public static BudgetControlsSettings Default =>
        Local(DefaultLocalPath);

    public static BudgetControlsSettings Local(string localPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        return new BudgetControlsSettings(
            LocalSource,
            CloudApiUrl: null,
            LocalPath: localPath.Trim(),
            HasCloudAuthKey: false,
            DateTimeOffset.UtcNow);
    }

    public static BudgetControlsSettings Cloud(
        string apiUrl,
        bool hasCloudAuthKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiUrl);

        return new BudgetControlsSettings(
            CloudSource,
            apiUrl.Trim(),
            LocalPath: null,
            hasCloudAuthKey,
            DateTimeOffset.UtcNow);
    }

    public static BudgetControlsSettings NormalizeOrDefault(BudgetControlsSettings? settings)
    {
        if (settings is null)
        {
            return Default;
        }

        if (string.Equals(settings.Source, CloudSource, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(settings.CloudApiUrl))
        {
            return settings with
            {
                Source = CloudSource,
                CloudApiUrl = settings.CloudApiUrl.Trim(),
                LocalPath = null
            };
        }

        string localPath = string.IsNullOrWhiteSpace(settings.LocalPath)
            ? DefaultLocalPath
            : settings.LocalPath.Trim();

        return settings with
        {
            Source = LocalSource,
            CloudApiUrl = null,
            LocalPath = localPath,
            HasCloudAuthKey = false
        };
    }
}
