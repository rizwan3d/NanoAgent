namespace NanoAgent.Application.Models;

public sealed record BudgetControlsPricing(
    decimal InputUsdPerMillionTokens,
    decimal CachedInputUsdPerMillionTokens,
    decimal OutputUsdPerMillionTokens);

public sealed record BudgetControlsLocalOptions(
    BudgetControlsPricing Pricing,
    decimal? MonthlyBudgetUsd,
    int AlertThresholdPercent);

public sealed record BudgetControlsUsageDelta(
    int InputTokens,
    int CachedInputTokens,
    int OutputTokens)
{
    public bool HasUsage => InputTokens > 0 || CachedInputTokens > 0 || OutputTokens > 0;
}

public sealed record BudgetControlsStatus(
    string Source,
    decimal? MonthlyBudgetUsd,
    decimal SpentUsd,
    int AlertThresholdPercent,
    string? LocalPath,
    string? CloudApiUrl,
    bool HasCloudAuthKey,
    bool Enabled = true)
{
    /// <summary>
    /// Status reported when budget controls are disabled (the default): no configuration was
    /// saved and no workspace <c>budget-controls.*.json</c> file exists.
    /// </summary>
    public static BudgetControlsStatus Disabled { get; } = new(
        BudgetControlsSettings.DisabledSource,
        MonthlyBudgetUsd: null,
        SpentUsd: 0m,
        AlertThresholdPercent: 80,
        LocalPath: null,
        CloudApiUrl: null,
        HasCloudAuthKey: false,
        Enabled: false);
}
