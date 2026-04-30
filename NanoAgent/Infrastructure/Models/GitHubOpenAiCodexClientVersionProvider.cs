using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Models;

internal sealed class GitHubOpenAiCodexClientVersionProvider : IOpenAiCodexClientVersionProvider
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/openai/codex/releases/latest";
    internal const string FallbackClientVersion = "0.125.0";

    private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromHours(6);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(15);

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubOpenAiCodexClientVersionProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _cachedClientVersion;
    private DateTimeOffset _cacheExpiresAt;

    public GitHubOpenAiCodexClientVersionProvider(
        HttpClient httpClient,
        ILogger<GitHubOpenAiCodexClientVersionProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> GetClientVersionAsync(CancellationToken cancellationToken)
    {
        if (TryGetCachedVersion() is { } cachedVersion)
        {
            return cachedVersion;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCachedVersion() is { } refreshedCachedVersion)
            {
                return refreshedCachedVersion;
            }

            string clientVersion = await FetchLatestClientVersionAsync(cancellationToken);
            Cache(clientVersion, SuccessCacheDuration);
            return clientVersion;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timed out while fetching the latest OpenAI Codex release version from GitHub. Falling back to {FallbackClientVersion}.",
                FallbackClientVersion);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to fetch the latest OpenAI Codex release version from GitHub. Falling back to {FallbackClientVersion}.",
                FallbackClientVersion);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "GitHub returned an invalid OpenAI Codex release response. Falling back to {FallbackClientVersion}.",
                FallbackClientVersion);
        }
        finally
        {
            _refreshLock.Release();
        }

        Cache(FallbackClientVersion, FailureCacheDuration);
        return FallbackClientVersion;
    }

    private async Task<string> FetchLatestClientVersionAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            LatestReleaseApiUrl,
            cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub returned HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 200)}");
        }

        using JsonDocument document = JsonDocument.Parse(responseBody);
        string tagName = TryGetString(document.RootElement, "tag_name")
            ?? throw new JsonException("GitHub did not return a release tag.");

        return NormalizeReleaseTag(tagName);
    }

    private string? TryGetCachedVersion()
    {
        if (!string.IsNullOrWhiteSpace(_cachedClientVersion) &&
            DateTimeOffset.UtcNow < _cacheExpiresAt)
        {
            return _cachedClientVersion;
        }

        return null;
    }

    private void Cache(
        string clientVersion,
        TimeSpan duration)
    {
        _cachedClientVersion = clientVersion;
        _cacheExpiresAt = DateTimeOffset.UtcNow.Add(duration);
    }

    private static string NormalizeReleaseTag(string tagName)
    {
        string normalized = tagName.Trim();
        int firstDigitIndex = normalized.IndexOfAny(
            ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']);
        if (firstDigitIndex >= 0)
        {
            normalized = normalized[firstDigitIndex..];
        }

        int metadataIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        int prereleaseIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0)
        {
            normalized = normalized[..prereleaseIndex];
        }

        return Version.TryParse(normalized, out _)
            ? normalized
            : throw new JsonException($"GitHub returned an invalid OpenAI Codex release tag: {tagName}");
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? value = property.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }
}
