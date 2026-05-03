using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.BudgetControls;

internal sealed class BudgetControlsUsageService : IBudgetControlsUsageService
{
    private const int DefaultAlertThresholdPercent = 80;
    private readonly HttpClient _httpClient;
    private readonly IBudgetControlsConfigurationStore _configurationStore;
    private readonly IBudgetControlsSecretStore _secretStore;

    public BudgetControlsUsageService(
        HttpClient httpClient,
        IBudgetControlsConfigurationStore configurationStore,
        IBudgetControlsSecretStore secretStore)
    {
        _httpClient = httpClient;
        _configurationStore = configurationStore;
        _secretStore = secretStore;
    }

    public async Task ConfigureLocalAsync(
        ReplSessionContext session,
        string? localPath,
        BudgetControlsLocalOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateLocalOptions(options);

        string fullPath = ResolveLocalPath(session.WorkspacePath, localPath);
        string relativePath = WorkspacePath.ToRelativePath(session.WorkspacePath, fullPath);
        LocalBudgetControlsDocument document = await LoadLocalDocumentAsync(
            fullPath,
            cancellationToken) ??
            new LocalBudgetControlsDocument();

        document.Source = "local";
        document.Pricing = new LocalBudgetControlsPricingDocument
        {
            InputUsdPerMillionTokens = options.Pricing.InputUsdPerMillionTokens,
            CachedInputUsdPerMillionTokens = options.Pricing.CachedInputUsdPerMillionTokens,
            OutputUsdPerMillionTokens = options.Pricing.OutputUsdPerMillionTokens
        };
        document.MonthlyBudgetUsd = options.MonthlyBudgetUsd;
        document.AlertThresholdPercent = NormalizeAlertThresholdPercent(options.AlertThresholdPercent);
        document.Usage ??= new LocalBudgetControlsUsageDocument();
        document.SpentUsd = Math.Max(0m, document.Usage.TotalCostUsd);
        document.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await SaveLocalDocumentAsync(fullPath, document, cancellationToken);
        await _configurationStore.SaveAsync(
            BudgetControlsSettings.Local(relativePath),
            cancellationToken);
    }

    public async Task<BudgetControlsStatus> GetStatusAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        BudgetControlsSettings settings = await LoadSettingsAsync(cancellationToken);
        if (string.Equals(settings.Source, BudgetControlsSettings.CloudSource, StringComparison.OrdinalIgnoreCase))
        {
            return await GetCloudStatusAsync(settings, cancellationToken);
        }

        string fullPath = ResolveLocalPath(session.WorkspacePath, settings.LocalPath);
        LocalBudgetControlsDocument? document = await LoadLocalDocumentAsync(
            fullPath,
            cancellationToken);

        return new BudgetControlsStatus(
            BudgetControlsSettings.LocalSource,
            document?.MonthlyBudgetUsd,
            document?.Usage?.TotalCostUsd ?? document?.SpentUsd ?? 0m,
            NormalizeAlertThresholdPercent(document?.AlertThresholdPercent),
            WorkspacePath.ToRelativePath(session.WorkspacePath, fullPath),
            CloudApiUrl: null,
            HasCloudAuthKey: false);
    }

    public async Task RecordUsageAsync(
        ReplSessionContext session,
        BudgetControlsUsageDelta usage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(usage);
        cancellationToken.ThrowIfCancellationRequested();

        BudgetControlsUsageDelta normalizedUsage = NormalizeUsage(usage);
        if (!normalizedUsage.HasUsage)
        {
            return;
        }

        BudgetControlsSettings settings = await LoadSettingsAsync(cancellationToken);
        if (string.Equals(settings.Source, BudgetControlsSettings.CloudSource, StringComparison.OrdinalIgnoreCase))
        {
            await PostCloudUsageAsync(settings, normalizedUsage, cancellationToken);
            return;
        }

        await RecordLocalUsageAsync(
            session,
            settings,
            normalizedUsage,
            cancellationToken);
    }

    private async Task RecordLocalUsageAsync(
        ReplSessionContext session,
        BudgetControlsSettings settings,
        BudgetControlsUsageDelta usage,
        CancellationToken cancellationToken)
    {
        string fullPath = ResolveLocalPath(session.WorkspacePath, settings.LocalPath);
        LocalBudgetControlsDocument document = await LoadLocalDocumentAsync(
            fullPath,
            cancellationToken) ??
            new LocalBudgetControlsDocument();

        LocalBudgetControlsPricingDocument pricing = document.Pricing ??
            new LocalBudgetControlsPricingDocument();
        LocalBudgetControlsUsageDocument totals = document.Usage ??
            new LocalBudgetControlsUsageDocument();

        int cachedInputTokens = Math.Clamp(
            usage.CachedInputTokens,
            0,
            usage.InputTokens);
        int billableInputTokens = Math.Max(0, usage.InputTokens - cachedInputTokens);
        decimal deltaCost =
            CalculateCost(billableInputTokens, pricing.InputUsdPerMillionTokens) +
            CalculateCost(cachedInputTokens, pricing.CachedInputUsdPerMillionTokens) +
            CalculateCost(usage.OutputTokens, pricing.OutputUsdPerMillionTokens);

        totals.InputTokens = AddClamped(totals.InputTokens, usage.InputTokens);
        totals.CachedInputTokens = AddClamped(totals.CachedInputTokens, cachedInputTokens);
        totals.OutputTokens = AddClamped(totals.OutputTokens, usage.OutputTokens);
        totals.TotalCostUsd = Math.Max(0m, totals.TotalCostUsd + deltaCost);
        totals.UpdatedAtUtc = DateTimeOffset.UtcNow;

        document.Source = "local";
        document.Pricing = pricing;
        document.Usage = totals;
        document.SpentUsd = totals.TotalCostUsd;
        document.AlertThresholdPercent = NormalizeAlertThresholdPercent(document.AlertThresholdPercent);
        document.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await SaveLocalDocumentAsync(
            fullPath,
            document,
            cancellationToken);
    }

    private async Task<BudgetControlsStatus> GetCloudStatusAsync(
        BudgetControlsSettings settings,
        CancellationToken cancellationToken)
    {
        string? authKey = await _secretStore.LoadCloudAuthKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.CloudApiUrl) ||
            string.IsNullOrWhiteSpace(authKey))
        {
            return new BudgetControlsStatus(
                BudgetControlsSettings.CloudSource,
                MonthlyBudgetUsd: null,
                SpentUsd: 0m,
                DefaultAlertThresholdPercent,
                LocalPath: null,
                settings.CloudApiUrl,
                HasCloudAuthKey: !string.IsNullOrWhiteSpace(authKey));
        }

        using HttpRequestMessage request = new(HttpMethod.Get, settings.CloudApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        BudgetControlsCloudStatusResponse? status = await JsonSerializer.DeserializeAsync(
            stream,
            BudgetControlsJsonContext.Default.BudgetControlsCloudStatusResponse,
            cancellationToken);

        return new BudgetControlsStatus(
            BudgetControlsSettings.CloudSource,
            status?.MonthlyBudgetUsd,
            status?.SpentUsd ?? 0m,
            NormalizeAlertThresholdPercent(status?.AlertThresholdPercent),
            LocalPath: null,
            settings.CloudApiUrl,
            HasCloudAuthKey: true);
    }

    private async Task PostCloudUsageAsync(
        BudgetControlsSettings settings,
        BudgetControlsUsageDelta usage,
        CancellationToken cancellationToken)
    {
        string? authKey = await _secretStore.LoadCloudAuthKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.CloudApiUrl) ||
            string.IsNullOrWhiteSpace(authKey))
        {
            return;
        }

        BudgetControlsUsageUpdateRequest payload = new(
            usage.InputTokens,
            usage.CachedInputTokens,
            usage.OutputTokens);
        string json = JsonSerializer.Serialize(
            payload,
            BudgetControlsJsonContext.Default.BudgetControlsUsageUpdateRequest);

        using HttpRequestMessage request = new(HttpMethod.Post, settings.CloudApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(responseJson))
        {
            _ = JsonSerializer.Deserialize(
                responseJson,
                BudgetControlsJsonContext.Default.BudgetControlsCloudStatusResponse);
        }
    }

    private async Task<BudgetControlsSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        return BudgetControlsSettings.NormalizeOrDefault(
            await _configurationStore.LoadAsync(cancellationToken));
    }

    private static async Task<LocalBudgetControlsDocument?> LoadLocalDocumentAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
        {
            return null;
        }

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        try
        {
            return await JsonSerializer.DeserializeAsync(
                stream,
                BudgetControlsJsonContext.Default.LocalBudgetControlsDocument,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task SaveLocalDocumentAsync(
        string fullPath,
        LocalBudgetControlsDocument document,
        CancellationToken cancellationToken)
    {
        string directoryPath = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Budget controls path does not contain a parent directory.");
        Directory.CreateDirectory(directoryPath);

        await using FileStream stream = new(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await JsonSerializer.SerializeAsync(
            stream,
            document,
            BudgetControlsJsonContext.Default.LocalBudgetControlsDocument,
            cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static string ResolveLocalPath(
        string workspacePath,
        string? localPath)
    {
        return WorkspacePath.Resolve(
            workspacePath,
            string.IsNullOrWhiteSpace(localPath)
                ? BudgetControlsSettings.DefaultLocalPath
                : localPath);
    }

    private static decimal CalculateCost(long tokens, decimal usdPerMillionTokens)
    {
        if (tokens <= 0 || usdPerMillionTokens <= 0m)
        {
            return 0m;
        }

        return tokens * usdPerMillionTokens / 1_000_000m;
    }

    private static long AddClamped(long current, int delta)
    {
        if (delta <= 0)
        {
            return current;
        }

        return current > long.MaxValue - delta
            ? long.MaxValue
            : current + delta;
    }

    private static BudgetControlsUsageDelta NormalizeUsage(BudgetControlsUsageDelta usage)
    {
        int inputTokens = Math.Max(0, usage.InputTokens);
        int cachedInputTokens = Math.Clamp(
            usage.CachedInputTokens,
            0,
            inputTokens);

        return new BudgetControlsUsageDelta(
            inputTokens,
            cachedInputTokens,
            Math.Max(0, usage.OutputTokens));
    }

    private static int NormalizeAlertThresholdPercent(int? value)
    {
        return Math.Clamp(
            value ?? DefaultAlertThresholdPercent,
            1,
            100);
    }

    private static void ValidateLocalOptions(BudgetControlsLocalOptions options)
    {
        BudgetControlsPricing pricing = options.Pricing;
        if (pricing.InputUsdPerMillionTokens < 0m ||
            pricing.CachedInputUsdPerMillionTokens < 0m ||
            pricing.OutputUsdPerMillionTokens < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pricing),
                "Budget prices cannot be negative.");
        }

        if (options.MonthlyBudgetUsd < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Monthly budget cannot be negative.");
        }
    }
}

internal sealed class LocalBudgetControlsDocument
{
    public int AlertThresholdPercent { get; set; } = 80;

    public decimal? MonthlyBudgetUsd { get; set; }

    public LocalBudgetControlsPricingDocument? Pricing { get; set; }

    public decimal SpentUsd { get; set; }

    public string Source { get; set; } = "local";

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public LocalBudgetControlsUsageDocument? Usage { get; set; }
}

internal sealed class LocalBudgetControlsPricingDocument
{
    public decimal CachedInputUsdPerMillionTokens { get; set; }

    public decimal InputUsdPerMillionTokens { get; set; }

    public decimal OutputUsdPerMillionTokens { get; set; }
}

internal sealed class LocalBudgetControlsUsageDocument
{
    public long CachedInputTokens { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public decimal TotalCostUsd { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed record BudgetControlsUsageUpdateRequest(
    int InputTokens,
    int CachedInputTokens,
    int OutputTokens);

internal sealed record BudgetControlsCloudStatusResponse(
    decimal? MonthlyBudgetUsd,
    decimal SpentUsd,
    int AlertThresholdPercent);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(LocalBudgetControlsDocument))]
[JsonSerializable(typeof(LocalBudgetControlsPricingDocument))]
[JsonSerializable(typeof(LocalBudgetControlsUsageDocument))]
[JsonSerializable(typeof(BudgetControlsUsageUpdateRequest))]
[JsonSerializable(typeof(BudgetControlsCloudStatusResponse))]
internal sealed partial class BudgetControlsJsonContext : JsonSerializerContext
{
}
