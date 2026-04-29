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

    private readonly HttpClient _httpClient;
    private readonly IOpenAiCodexClientVersionProvider _openAiCodexClientVersionProvider;
    private readonly IOpenAiChatGptAccountCredentialService? _openAiChatGptAccountCredentialService;
    private readonly ILogger<OpenAiCompatibleModelProviderClient> _logger;

    public OpenAiCompatibleModelProviderClient(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleModelProviderClient> logger,
        IOpenAiChatGptAccountCredentialService? openAiChatGptAccountCredentialService = null,
        IOpenAiCodexClientVersionProvider? openAiCodexClientVersionProvider = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _openAiChatGptAccountCredentialService = openAiChatGptAccountCredentialService;
        _openAiCodexClientVersionProvider = openAiCodexClientVersionProvider ??
            new StaticOpenAiCodexClientVersionProvider(
                GitHubOpenAiCodexClientVersionProvider.FallbackClientVersion);
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
        string accountClientVersion = await _openAiCodexClientVersionProvider.GetClientVersionAsync(
            cancellationToken);

        bool forcedRefreshAfterAuthFailure = false;

        while (true)
        {
            try
            {
                using HttpRequestMessage request = CreateOpenAiChatGptAccountModelsRequest(
                    baseUri,
                    credential,
                    accountClientVersion);
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
                    string detail = string.IsNullOrWhiteSpace(responseBody)
                        ? $"Provider returned HTTP {(int)response.StatusCode}."
                        : $"Provider returned HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 200)}";

                    throw new ModelProviderException(
                        $"Unable to fetch OpenAI ChatGPT Plus/Pro models from the account API. {detail}");
                }

                IReadOnlyList<AvailableModel> models = ParseAvailableModels(responseBody);
                if (models.Count > 0)
                {
                    return models;
                }

                throw new ModelProviderException(
                    "The OpenAI ChatGPT Plus/Pro account API returned an invalid models response.");
            }
            catch (HttpRequestException exception)
            {
                throw new ModelProviderException(
                    "Unable to fetch OpenAI ChatGPT Plus/Pro models from the account API.",
                    exception);
            }
            catch (JsonException exception)
            {
                throw new ModelProviderException(
                    "The OpenAI ChatGPT Plus/Pro account API returned an invalid models response.",
                    exception);
            }
        }
    }

    private HttpRequestMessage CreateOpenAiChatGptAccountModelsRequest(
        Uri baseUri,
        OpenAiChatGptAccountResolvedCredential credential,
        string accountClientVersion)
    {
        HttpRequestMessage request = new(
            HttpMethod.Get,
            new Uri(baseUri, $"models?client_version={Uri.EscapeDataString(accountClientVersion)}"));
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

    private sealed class StaticOpenAiCodexClientVersionProvider : IOpenAiCodexClientVersionProvider
    {
        private readonly string _clientVersion;

        public StaticOpenAiCodexClientVersionProvider(string clientVersion)
        {
            _clientVersion = clientVersion;
        }

        public Task<string> GetClientVersionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_clientVersion);
        }
    }
}
