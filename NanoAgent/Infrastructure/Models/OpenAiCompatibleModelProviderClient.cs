using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Anthropic;
using NanoAgent.Infrastructure.GitHub;
using NanoAgent.Infrastructure.OpenAi;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Models;

internal sealed class OpenAiCompatibleModelProviderClient : IModelProviderClient
{
    private const string AnthropicVersion = "2023-06-01";
    private const string AnthropicClaudeAccountBetaHeader = "claude-code-20250219,oauth-2025-04-20";
    private const string AnthropicClaudeAccountUserAgent = "claude-cli/2.1.75";
    private const string AccountHeaderName = "Chat" + "G" + "P" + "T-Account-Id";
    private const string Originator = "nanoagent";
    private const string OpenRouterApplicationTitle = "NanoAgent";
    private const string OpenRouterApplicationUrl = "https://github.com/rizwan3d/NanoAgent";
    private static readonly string[] ContextWindowPropertyNames =
    [
        "context_length",
        "contextLength",
        "context_window",
        "contextWindow",
        "context_window_tokens",
        "contextWindowTokens",
        "max_context_length",
        "maxContextLength",
        "max_context_tokens",
        "maxContextTokens",
        "input_token_limit",
        "inputTokenLimit",
        "max_input_tokens",
        "maxInputTokens"
    ];
    private static readonly string[] ContextWindowContainerPropertyNames =
    [
        "metadata",
        "limits",
        "capabilities",
        "architecture",
        "top_provider",
        "topProvider"
    ];

    private readonly HttpClient _httpClient;
    private readonly IOpenAiCodexClientVersionProvider _openAiCodexClientVersionProvider;
    private readonly IAnthropicClaudeAccountCredentialService? _anthropicClaudeAccountCredentialService;
    private readonly IGitHubCopilotCredentialService? _gitHubCopilotCredentialService;
    private readonly IOpenAiChatGptAccountCredentialService? _openAiChatGptAccountCredentialService;
    private readonly ILogger<OpenAiCompatibleModelProviderClient> _logger;

    public OpenAiCompatibleModelProviderClient(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleModelProviderClient> logger,
        IOpenAiChatGptAccountCredentialService? openAiChatGptAccountCredentialService = null,
        IOpenAiCodexClientVersionProvider? openAiCodexClientVersionProvider = null,
        IAnthropicClaudeAccountCredentialService? anthropicClaudeAccountCredentialService = null,
        IGitHubCopilotCredentialService? gitHubCopilotCredentialService = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _openAiChatGptAccountCredentialService = openAiChatGptAccountCredentialService;
        _anthropicClaudeAccountCredentialService = anthropicClaudeAccountCredentialService;
        _gitHubCopilotCredentialService = gitHubCopilotCredentialService;
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

        if (providerProfile.ProviderKind == ProviderKind.AnthropicClaudeAccount)
        {
            if (_anthropicClaudeAccountCredentialService is null)
            {
                throw new ModelProviderException(
                    "Anthropic Claude Pro/Max credentials cannot be resolved in this runtime.");
            }

            return await GetAnthropicClaudeAccountModelsAsync(
                apiKey,
                cancellationToken);
        }

        if (providerProfile.ProviderKind == ProviderKind.GitHubCopilot)
        {
            if (_gitHubCopilotCredentialService is null)
            {
                throw new ModelProviderException(
                    "GitHub Copilot credentials cannot be resolved in this runtime.");
            }

            return await GetGitHubCopilotModelsAsync(
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

    private async Task<IReadOnlyList<AvailableModel>> GetAnthropicClaudeAccountModelsAsync(
        string storedCredentials,
        CancellationToken cancellationToken)
    {
        if (_anthropicClaudeAccountCredentialService is null)
        {
            throw new ModelProviderException(
                "Anthropic Claude Pro/Max credentials cannot be resolved in this runtime.");
        }

        Uri baseUri = new AgentProviderProfile(
            ProviderKind.AnthropicClaudeAccount,
            BaseUrl: null).ResolveBaseUri();
        AnthropicClaudeAccountResolvedCredential credential =
            await _anthropicClaudeAccountCredentialService.ResolveAsync(
                storedCredentials,
                forceRefresh: false,
                cancellationToken);

        bool forcedRefreshAfterAuthFailure = false;

        while (true)
        {
            try
            {
                using HttpRequestMessage request = CreateAnthropicClaudeAccountModelsRequest(
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
                    credential = await _anthropicClaudeAccountCredentialService.ResolveAsync(
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
                        $"Unable to fetch Anthropic Claude Pro/Max models from the account API. {detail}");
                }

                IReadOnlyList<AvailableModel> models = ParseAvailableModels(responseBody);
                if (models.Count > 0)
                {
                    return models;
                }

                throw new ModelProviderException(
                    "The Anthropic Claude Pro/Max account API returned an invalid models response.");
            }
            catch (HttpRequestException exception)
            {
                throw new ModelProviderException(
                    "Unable to fetch Anthropic Claude Pro/Max models from the account API.",
                    exception);
            }
            catch (JsonException exception)
            {
                throw new ModelProviderException(
                    "The Anthropic Claude Pro/Max account API returned an invalid models response.",
                    exception);
            }
        }
    }

    private async Task<IReadOnlyList<AvailableModel>> GetGitHubCopilotModelsAsync(
        string storedCredentials,
        CancellationToken cancellationToken)
    {
        if (_gitHubCopilotCredentialService is null)
        {
            throw new ModelProviderException(
                "GitHub Copilot credentials cannot be resolved in this runtime.");
        }

        GitHubCopilotResolvedCredential credential =
            await _gitHubCopilotCredentialService.ResolveAsync(
                storedCredentials,
                forceRefresh: false,
                cancellationToken);

        bool forcedRefreshAfterAuthFailure = false;

        while (true)
        {
            try
            {
                using HttpRequestMessage request = CreateGitHubCopilotModelsRequest(credential);
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
                    credential = await _gitHubCopilotCredentialService.ResolveAsync(
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
                        $"Unable to fetch GitHub Copilot models from the account API. {detail}");
                }

                IReadOnlyList<AvailableModel> models = ParseAvailableModels(responseBody);
                if (models.Count > 0)
                {
                    return models;
                }

                throw new ModelProviderException(
                    "The GitHub Copilot account API returned an invalid models response.");
            }
            catch (HttpRequestException exception)
            {
                throw new ModelProviderException(
                    "Unable to fetch GitHub Copilot models from the account API.",
                    exception);
            }
            catch (JsonException exception)
            {
                throw new ModelProviderException(
                    "The GitHub Copilot account API returned an invalid models response.",
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

    private static HttpRequestMessage CreateAnthropicClaudeAccountModelsRequest(
        Uri baseUri,
        AnthropicClaudeAccountResolvedCredential credential)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUri, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        request.Headers.TryAddWithoutValidation("anthropic-beta", AnthropicClaudeAccountBetaHeader);
        request.Headers.TryAddWithoutValidation("User-Agent", AnthropicClaudeAccountUserAgent);
        request.Headers.TryAddWithoutValidation("x-app", "cli");
        return request;
    }

    private static HttpRequestMessage CreateGitHubCopilotModelsRequest(
        GitHubCopilotResolvedCredential credential)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(credential.BaseUri, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.Accept.ParseAdd("application/json");
        GitHubCopilotCredentialService.ApplyCopilotHeaders(request);
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
                models.Add(new AvailableModel(
                    id.Trim(),
                    TryGetContextWindowTokens(item)));
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

    private static int? TryGetContextWindowTokens(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in item.EnumerateObject())
        {
            if (MatchesAny(property.Name, ContextWindowPropertyNames) &&
                TryReadPositiveInt32(property.Value, out int tokens))
            {
                return tokens;
            }
        }

        foreach (JsonProperty property in item.EnumerateObject())
        {
            if (MatchesAny(property.Name, ContextWindowContainerPropertyNames) &&
                TryGetContextWindowTokens(property.Value) is { } nestedTokens)
            {
                return nestedTokens;
            }
        }

        return null;
    }

    private static bool TryReadPositiveInt32(JsonElement value, out int result)
    {
        result = 0;
        long numericValue;

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetInt64(out numericValue))
            {
                return false;
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            string? rawValue = value.GetString();
            if (!long.TryParse(
                    rawValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out numericValue))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        if (numericValue <= 0 || numericValue > int.MaxValue)
        {
            return false;
        }

        result = (int)numericValue;
        return true;
    }

    private static bool MatchesAny(string value, IReadOnlyList<string> candidates)
    {
        return candidates.Any(candidate => string.Equals(
            candidate,
            value,
            StringComparison.OrdinalIgnoreCase));
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
