using System.Net.Http.Headers;
using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.OpenAi;
using Microsoft.Extensions.Logging;

namespace NanoAgent.Infrastructure.Models;

internal sealed class OpenAiCompatibleModelProviderClient : IModelProviderClient
{
    private const string AnthropicVersion = "2023-06-01";
    private const string AccountHeaderName = "Chat" + "G" + "P" + "T-Account-Id";
    private const string Originator = "nanoagent";
    private const string OpenRouterApplicationTitle = "NanoAgent";
    private const string OpenRouterApplicationUrl = "https://github.com/rizwan3d/NanoAgent";
    private static readonly AvailableModel[] OpenAiChatGptAccountModels =
    [
        new("gpt-5.3-codex"),
        new("gpt-5.3-codex-spark"),
        new("gpt-5.5"),
        new("gpt-5.4"),
        new("gpt-5.4-mini"),
        new("gpt-5.2"),
        new("gpt-5.2-codex"),
        new("gpt-5.1"),
        new("gpt-5.1-codex-max"),
        new("gpt-5.1-codex"),
        new("gpt-5.1-codex-mini"),
        new("gpt-5"),
        new("gpt-5-codex"),
        new("gpt-5-codex-mini")
    ];

    private readonly HttpClient _httpClient;
    private readonly IOpenAiChatGptAccountCredentialService? _openAiChatGptAccountCredentialService;
    private readonly ILogger<OpenAiCompatibleModelProviderClient> _logger;

    public OpenAiCompatibleModelProviderClient(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleModelProviderClient> logger,
        IOpenAiChatGptAccountCredentialService? openAiChatGptAccountCredentialService = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _openAiChatGptAccountCredentialService = openAiChatGptAccountCredentialService;
    }

    public async Task<IReadOnlyList<AvailableModel>> GetAvailableModelsAsync(
        AgentProviderProfile providerProfile,
        string apiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        if (providerProfile.ProviderKind == ProviderKind.OpenAiChatGptAccount)
        {
            if (_openAiChatGptAccountCredentialService is null)
            {
                throw new ModelProviderException(
                    "OpenAI ChatGPT Plus/Pro credentials cannot be resolved in this runtime.");
            }

            return await GetOpenAiChatGptAccountModelsAsync(
                apiKey,
                cancellationToken);
        }

        Uri baseUri = providerProfile.ResolveBaseUri();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUri, "models"));
        ApplyAuthenticationHeaders(request, providerProfile.ProviderKind, apiKey);
        LogDebugApiRequest(request.Method, request.RequestUri);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        LogDebugApiResponse(response.StatusCode, responseBody);

        if (!response.IsSuccessStatusCode)
        {
            string detail = string.IsNullOrWhiteSpace(responseBody)
                ? $"Provider returned HTTP {(int)response.StatusCode}."
                : $"Provider returned HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 200)}";

            throw new ModelProviderException(
                $"Unable to fetch models from the configured provider. {detail}");
        }

        IReadOnlyList<AvailableModel> models = ParseAvailableModels(responseBody);
        if (models.Count == 0)
        {
            throw new ModelProviderException(
                "The configured provider returned an invalid models response.");
        }

        return models;
    }

    private async Task<IReadOnlyList<AvailableModel>> GetOpenAiChatGptAccountModelsAsync(
        string storedCredentials,
        CancellationToken cancellationToken)
    {
        if (_openAiChatGptAccountCredentialService is null)
        {
            throw new ModelProviderException(
                "OpenAI ChatGPT Plus/Pro credentials cannot be resolved in this runtime.");
        }

        Uri baseUri = new AgentProviderProfile(
            ProviderKind.OpenAiChatGptAccount,
            BaseUrl: null).ResolveBaseUri();
        OpenAiChatGptAccountResolvedCredential credential =
            await _openAiChatGptAccountCredentialService.ResolveAsync(
                storedCredentials,
                forceRefresh: false,
                cancellationToken);

        bool forcedRefreshAfterAuthFailure = false;

        while (true)
        {
            try
            {
                using HttpRequestMessage request = CreateOpenAiChatGptAccountModelsRequest(
                    baseUri,
                    credential);
                LogDebugApiRequest(request.Method, request.RequestUri);

                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                LogDebugApiResponse(response.StatusCode, responseBody);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
                    !forcedRefreshAfterAuthFailure)
                {
                    forcedRefreshAfterAuthFailure = true;
                    credential = await _openAiChatGptAccountCredentialService.ResolveAsync(
                        storedCredentials,
                        forceRefresh: true,
                        cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    LogOpenAiChatGptAccountModelFallback(
                        $"HTTP {(int)response.StatusCode}");
                    return OpenAiChatGptAccountModels;
                }

                IReadOnlyList<AvailableModel> models = ParseAvailableModels(responseBody);
                if (models.Count > 0)
                {
                    return models;
                }

                LogOpenAiChatGptAccountModelFallback("empty or invalid model response");
                return OpenAiChatGptAccountModels;
            }
            catch (HttpRequestException exception)
            {
                LogOpenAiChatGptAccountModelFallback(exception.Message);
                return OpenAiChatGptAccountModels;
            }
            catch (JsonException exception)
            {
                LogOpenAiChatGptAccountModelFallback(exception.Message);
                return OpenAiChatGptAccountModels;
            }
        }
    }

    private HttpRequestMessage CreateOpenAiChatGptAccountModelsRequest(
        Uri baseUri,
        OpenAiChatGptAccountResolvedCredential credential)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUri, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.TryAddWithoutValidation("originator", Originator);
        request.Headers.TryAddWithoutValidation("User-Agent", "NanoAgent/1.0");
        if (!string.IsNullOrWhiteSpace(credential.AccountId))
        {
            request.Headers.TryAddWithoutValidation(AccountHeaderName, credential.AccountId);
        }

        return request;
    }

    private static void ApplyAuthenticationHeaders(
        HttpRequestMessage request,
        ProviderKind providerKind,
        string apiKey)
    {
        if (providerKind == ProviderKind.Anthropic)
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        if (providerKind == ProviderKind.OpenRouter)
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", OpenRouterApplicationUrl);
            request.Headers.TryAddWithoutValidation("X-Title", OpenRouterApplicationTitle);
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static IReadOnlyList<AvailableModel> ParseAvailableModels(string responseBody)
    {
        using JsonDocument document = JsonDocument.Parse(responseBody);
        if (!TryGetModelArray(document.RootElement, out JsonElement modelsElement))
        {
            return [];
        }

        List<AvailableModel> models = [];
        foreach (JsonElement item in modelsElement.EnumerateArray())
        {
            string? id = TryGetModelId(item);
            if (!string.IsNullOrWhiteSpace(id))
            {
                models.Add(new AvailableModel(id.Trim()));
            }
        }

        return models;
    }

    private static bool TryGetModelArray(
        JsonElement root,
        out JsonElement modelsElement)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            modelsElement = root;
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out modelsElement) &&
            modelsElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("models", out modelsElement) &&
            modelsElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        modelsElement = default;
        return false;
    }

    private static string? TryGetModelId(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            return item.GetString();
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (string propertyName in new[] { "id", "slug", "model_slug" })
        {
            if (item.TryGetProperty(propertyName, out JsonElement property) &&
                property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private void LogOpenAiChatGptAccountModelFallback(string reason)
    {
        _logger.LogWarning(
            "Unable to fetch account-backed model list dynamically. Using fallback models. Reason: {Reason}",
            reason);
    }

    private void LogDebugApiRequest(
        HttpMethod method,
        Uri? requestUri)
    {
#if DEBUG
        _logger.LogInformation(
            "OpenAI-compatible models API request {Method} {RequestUri}",
            method,
            requestUri);
#endif
    }

    private void LogDebugApiResponse(
        System.Net.HttpStatusCode statusCode,
        string responseBody)
    {
#if DEBUG
        _logger.LogInformation(
            "OpenAI-compatible models API response {StatusCode}: {ResponseBody}",
            (int)statusCode,
            responseBody);
#endif
    }
}
