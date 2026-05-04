using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NanoAgent.Infrastructure.GitHub;

internal sealed class GitHubCopilotCredentialService :
    IGitHubCopilotAuthenticator,
    IGitHubCopilotCredentialService
{
    private const string ClientId = "Iv1.b507a08c87ecfe98";
    private const string CredentialType = "github-copilot";
    private const string DefaultDomain = "github.com";
    private static readonly TimeSpan TokenExpiryBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IStatusMessageWriter _statusMessageWriter;
    private readonly ITextPrompt _textPrompt;
    private readonly ILogger<GitHubCopilotCredentialService> _logger;

    public GitHubCopilotCredentialService(
        HttpClient httpClient,
        IApiKeySecretStore secretStore,
        ITextPrompt textPrompt,
        IStatusMessageWriter statusMessageWriter,
        ILogger<GitHubCopilotCredentialService> logger)
    {
        _httpClient = httpClient;
        _secretStore = secretStore;
        _textPrompt = textPrompt;
        _statusMessageWriter = statusMessageWriter;
        _logger = logger;
    }

    public async Task<string> AuthenticateAsync(CancellationToken cancellationToken)
    {
        string enterpriseInput = await _textPrompt.PromptAsync(
            new TextPromptRequest(
                "GitHub Enterprise URL/domain",
                "Leave blank for github.com. Enter a hostname or URL only when your Copilot account uses GitHub Enterprise.",
                DefaultValue: null),
            cancellationToken);
        string? enterpriseDomain = NormalizeDomain(enterpriseInput);
        if (!string.IsNullOrWhiteSpace(enterpriseInput) && enterpriseDomain is null)
        {
            throw new PromptCancelledException("Invalid GitHub Enterprise URL/domain.");
        }

        string domain = enterpriseDomain ?? DefaultDomain;
        GitHubDeviceCodeResponse device = await StartDeviceFlowAsync(domain, cancellationToken);

        await _statusMessageWriter.ShowInfoAsync(
            "Opening browser for GitHub Copilot device sign-in.",
            cancellationToken);
        await _statusMessageWriter.ShowInfoAsync(
            $"Enter code {device.UserCode} at {device.VerificationUri}",
            cancellationToken);

        if (!TryOpenBrowser(device.VerificationUri))
        {
            _logger.LogInformation("Unable to open the system browser for GitHub Copilot sign-in.");
        }

        string githubAccessToken = await PollForGitHubAccessTokenAsync(
            domain,
            device,
            cancellationToken);
        GitHubCopilotCredentials credentials = await RefreshCredentialsAsync(
            githubAccessToken,
            enterpriseDomain,
            cancellationToken);

        await _statusMessageWriter.ShowSuccessAsync(
            "GitHub Copilot sign-in completed.",
            cancellationToken);

        return SerializeCredentials(credentials);
    }

    public async Task<GitHubCopilotResolvedCredential> ResolveAsync(
        string storedCredentials,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedCredentials);

        string credentialsJson = storedCredentials;
        if (forceRefresh)
        {
            credentialsJson = await _secretStore.LoadAsync(cancellationToken) ?? storedCredentials;
        }

        GitHubCopilotCredentials credentials = DeserializeCredentials(credentialsJson);
        if (forceRefresh || IsExpired(credentials))
        {
            credentials = await RefreshCredentialsAsync(
                credentials.RefreshToken,
                credentials.EnterpriseDomain,
                cancellationToken);
            await _secretStore.SaveAsync(SerializeCredentials(credentials), cancellationToken);
        }

        return new GitHubCopilotResolvedCredential(
            credentials.AccessToken,
            credentials.EnterpriseDomain,
            CreateBaseUri(credentials.BaseUrl));
    }

    internal static string? NormalizeDomain(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        string trimmed = input.Trim();
        try
        {
            Uri uri = trimmed.Contains("://", StringComparison.Ordinal)
                ? new Uri(trimmed)
                : new Uri($"https://{trimmed}");

            return string.IsNullOrWhiteSpace(uri.Host)
                ? null
                : uri.Host.Trim().ToLowerInvariant();
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    internal static string GetBaseUrlFromToken(string? token, string? enterpriseDomain)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            const string marker = "proxy-ep=";
            int markerIndex = token.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                int hostStart = markerIndex + marker.Length;
                int hostEnd = token.IndexOf(';', hostStart);
                string proxyHost = hostEnd < 0
                    ? token[hostStart..]
                    : token[hostStart..hostEnd];
                if (!string.IsNullOrWhiteSpace(proxyHost))
                {
                    string apiHost = proxyHost.Trim().StartsWith("proxy.", StringComparison.OrdinalIgnoreCase)
                        ? "api." + proxyHost.Trim()[6..]
                        : proxyHost.Trim();
                    return $"https://{apiHost}";
                }
            }
        }

        return string.IsNullOrWhiteSpace(enterpriseDomain)
            ? "https://api.individual.githubcopilot.com"
            : $"https://copilot-api.{enterpriseDomain.Trim()}";
    }

    private async Task<GitHubDeviceCodeResponse> StartDeviceFlowAsync(
        string domain,
        CancellationToken cancellationToken)
    {
        Uri endpoint = new($"https://{domain}/login/device/code");
        using FormUrlEncodedContent content = new(
        [
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("scope", "read:user")
        ]);
        using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
        {
            Content = content
        };
        request.Headers.Accept.ParseAdd("application/json");
        ApplyGitHubUserAgent(request);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub Copilot device-code request failed with HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 300)}");
        }

        using JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;
        string deviceCode = RequireString(root, "device_code", "device code response");
        string userCode = RequireString(root, "user_code", "device code response");
        string verificationUri = RequireString(root, "verification_uri", "device code response");
        int intervalSeconds = RequirePositiveInt32(root, "interval", "device code response");
        int expiresInSeconds = RequirePositiveInt32(root, "expires_in", "device code response");

        return new GitHubDeviceCodeResponse(
            deviceCode,
            userCode,
            verificationUri,
            intervalSeconds,
            expiresInSeconds);
    }

    private async Task<string> PollForGitHubAccessTokenAsync(
        string domain,
        GitHubDeviceCodeResponse device,
        CancellationToken cancellationToken)
    {
        Uri endpoint = new($"https://{domain}/login/oauth/access_token");
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(device.ExpiresInSeconds);
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, device.IntervalSeconds));
        int slowDownResponses = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            TimeSpan delay = remaining < interval ? remaining : interval;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            using FormUrlEncodedContent content = new(
            [
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("device_code", device.DeviceCode),
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
            ]);
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            request.Headers.Accept.ParseAdd("application/json");
            ApplyGitHubUserAgent(request);

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"GitHub Copilot device-token request failed with HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 300)}");
            }

            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (TryGetString(root, "access_token") is string accessToken)
            {
                return accessToken;
            }

            string? error = TryGetString(root, "error");
            if (string.Equals(error, "authorization_pending", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(error, "slow_down", StringComparison.Ordinal))
            {
                slowDownResponses++;
                int? serverInterval = TryGetInt32(root, "interval");
                interval = serverInterval is > 0
                    ? TimeSpan.FromSeconds(serverInterval.Value)
                    : interval + TimeSpan.FromSeconds(5);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                string? description = TryGetString(root, "error_description");
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(description)
                        ? $"GitHub Copilot device flow failed: {error}"
                        : $"GitHub Copilot device flow failed: {error}: {description}");
            }
        }

        if (slowDownResponses > 0)
        {
            throw new TimeoutException(
                "GitHub Copilot device flow timed out after slow_down responses. Check system clock sync and try again.");
        }

        throw new TimeoutException("GitHub Copilot device flow timed out.");
    }

    private async Task<GitHubCopilotCredentials> RefreshCredentialsAsync(
        string refreshToken,
        string? enterpriseDomain,
        CancellationToken cancellationToken)
    {
        string domain = enterpriseDomain ?? DefaultDomain;
        Uri endpoint = new($"https://api.{domain}/copilot_internal/v2/token");
        using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshToken);
        ApplyCopilotHeaders(request);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub Copilot token request failed with HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 300)}");
        }

        using JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;
        string accessToken = RequireString(root, "token", "Copilot token response");
        int expiresAtSeconds = RequirePositiveInt32(root, "expires_at", "Copilot token response");

        return new GitHubCopilotCredentials(
            CredentialType,
            accessToken,
            refreshToken,
            DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds).ToUnixTimeMilliseconds(),
            enterpriseDomain,
            GetBaseUrlFromToken(accessToken, enterpriseDomain));
    }

    internal static void ApplyCopilotHeaders(HttpRequestMessage request)
    {
        ApplyGitHubUserAgent(request);
        request.Headers.TryAddWithoutValidation("Editor-Version", "vscode/1.107.0");
        request.Headers.TryAddWithoutValidation("Editor-Plugin-Version", "copilot-chat/0.35.0");
        request.Headers.TryAddWithoutValidation("Copilot-Integration-Id", "vscode-chat");
    }

    private static void ApplyGitHubUserAgent(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", "GitHubCopilotChat/0.35.0");
    }

    private static GitHubCopilotCredentials DeserializeCredentials(string value)
    {
        try
        {
            GitHubCopilotCredentials? credentials = JsonSerializer.Deserialize(
                value,
                GitHubCopilotJsonContext.Default.GitHubCopilotCredentials);

            if (credentials is null ||
                !string.Equals(credentials.Type, CredentialType, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(credentials.AccessToken) ||
                string.IsNullOrWhiteSpace(credentials.RefreshToken) ||
                string.IsNullOrWhiteSpace(credentials.BaseUrl))
            {
                throw new InvalidOperationException();
            }

            return credentials;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Stored GitHub Copilot credentials are invalid. Run onboarding again to sign in.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "Stored GitHub Copilot credentials are incomplete. Run onboarding again to sign in.",
                exception);
        }
    }

    private static string SerializeCredentials(GitHubCopilotCredentials credentials)
    {
        return JsonSerializer.Serialize(
            credentials,
            GitHubCopilotJsonContext.Default.GitHubCopilotCredentials);
    }

    private static bool IsExpired(GitHubCopilotCredentials credentials)
    {
        long refreshAfter = credentials.ExpiresUnixMilliseconds - (long)TokenExpiryBuffer.TotalMilliseconds;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= refreshAfter;
    }

    private static Uri CreateBaseUri(string baseUrl)
    {
        string normalized = baseUrl.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl
            : $"{baseUrl}/";

        return new Uri(normalized);
    }

    private static string RequireString(JsonElement root, string propertyName, string responseName)
    {
        return TryGetString(root, propertyName)
            ?? throw new InvalidOperationException($"GitHub Copilot {responseName} was missing '{propertyName}'.");
    }

    private static int RequirePositiveInt32(JsonElement root, string propertyName, string responseName)
    {
        int? value = TryGetInt32(root, propertyName);
        if (value is not > 0)
        {
            throw new InvalidOperationException($"GitHub Copilot {responseName} was missing '{propertyName}'.");
        }

        return value.Value;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out int value))
        {
            return null;
        }

        return value;
    }

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }

            string fileName = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            Process.Start(new ProcessStartInfo(fileName, url) { UseShellExecute = false });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private sealed record GitHubDeviceCodeResponse(
        string DeviceCode,
        string UserCode,
        string VerificationUri,
        int IntervalSeconds,
        int ExpiresInSeconds);
}
