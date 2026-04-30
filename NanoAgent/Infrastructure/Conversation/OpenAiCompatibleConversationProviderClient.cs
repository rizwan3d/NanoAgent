using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.OpenAi;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class OpenAiCompatibleConversationProviderClient : IConversationProviderClient
{
    private const int MaxRetryAttempts = 3;
    private const string OpenRouterApplicationTitle = "NanoAgent";
    private const string OpenRouterApplicationUrl = "https://github.com/rizwan3d/NanoAgent";
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly HttpClient _httpClient;
    private readonly Func<double> _nextJitter;
    private readonly IOpenAiChatGptAccountCredentialService? _openAiChatGptAccountCredentialService;
    private readonly ILogger<OpenAiCompatibleConversationProviderClient> _logger;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");

    public OpenAiCompatibleConversationProviderClient(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleConversationProviderClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<double>? nextJitter = null,
        IOpenAiChatGptAccountCredentialService? openAiChatGptAccountCredentialService = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
        _nextJitter = nextJitter ?? Random.Shared.NextDouble;
        _openAiChatGptAccountCredentialService = openAiChatGptAccountCredentialService;
    }

    public async Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ProviderProfile.ProviderKind == ProviderKind.OpenAiChatGptAccount)
        {
            return await SendOpenAiChatGptAccountAsync(request, cancellationToken);
        }

        OpenAiChatCompletionRequest payload = BuildRequestPayload(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.OpenAiChatCompletionRequest);

        Uri baseUri = request.ProviderProfile.ResolveBaseUri();
        int retryCount = 0;

        for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
        {
            using HttpRequestMessage httpRequest = CreateHttpRequest(
                baseUri,
                request.ProviderProfile.ProviderKind,
                request.ApiKey,
                requestBody);
            LogDebugApiRequest(httpRequest.Method, httpRequest.RequestUri, requestBody);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            LogDebugApiResponse(response.StatusCode, TryGetResponseId(response), responseBody);

            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    throw new ConversationProviderException(
                        "The provider returned an empty response body for the conversation request.");
                }

                return new ConversationProviderPayload(
                    request.ProviderProfile.ProviderKind,
                    responseBody,
                    TryGetResponseId(response),
                    retryCount);
            }

            if (IsRetryableStatusCode(response.StatusCode) && attempt < MaxRetryAttempts)
            {
                retryCount++;
                TimeSpan retryDelay = CalculateRetryDelay(retryCount, response.Headers.RetryAfter);
                _logger.LogWarning(
                    "Provider returned retryable HTTP {StatusCode} on attempt {Attempt} of {MaxAttempts}. Retrying after {RetryDelayMilliseconds} ms.",
                    (int)response.StatusCode,
                    attempt + 1,
                    MaxRetryAttempts + 1,
                    Math.Round(retryDelay.TotalMilliseconds, MidpointRounding.AwayFromZero));
                await _delayAsync(retryDelay, cancellationToken);
                continue;
            }

            ThrowConversationRequestFailed(response.StatusCode, responseBody);
        }

        throw new ConversationProviderException(
            "Unable to complete the conversation request. The provider retry loop ended unexpectedly.");
    }

    private async Task<ConversationProviderPayload> SendOpenAiChatGptAccountAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (_openAiChatGptAccountCredentialService is null)
        {
            throw new ConversationProviderException(
                "OpenAI ChatGPT Plus/Pro credentials cannot be resolved in this runtime.");
        }

        OpenAiResponsesRequest payload = BuildResponsesRequestPayload(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.OpenAiResponsesRequest);
        Uri baseUri = request.ProviderProfile.ResolveBaseUri();
        OpenAiChatGptAccountResolvedCredential credential =
            await _openAiChatGptAccountCredentialService.ResolveAsync(
                request.ApiKey,
                forceRefresh: false,
                cancellationToken);
        int retryCount = 0;
        bool forcedRefreshAfterAuthFailure = false;

        for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
        {
            using HttpRequestMessage httpRequest = CreateOpenAiChatGptAccountHttpRequest(
                baseUri,
                credential,
                requestBody);
            LogDebugApiRequest(httpRequest.Method, httpRequest.RequestUri, requestBody);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            LogDebugApiResponse(response.StatusCode, TryGetResponseId(response), responseBody);

            if (response.IsSuccessStatusCode)
            {
                string normalizedResponseBody = NormalizeOpenAiChatGptAccountResponseBody(responseBody);
                if (string.IsNullOrWhiteSpace(normalizedResponseBody))
                {
                    throw new ConversationProviderException(
                        "The provider returned an empty response body for the conversation request.");
                }

                return new ConversationProviderPayload(
                    request.ProviderProfile.ProviderKind,
                    normalizedResponseBody,
                    TryGetResponseId(response),
                    retryCount);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !forcedRefreshAfterAuthFailure)
            {
                forcedRefreshAfterAuthFailure = true;
                credential = await _openAiChatGptAccountCredentialService.ResolveAsync(
                    request.ApiKey,
                    forceRefresh: true,
                    cancellationToken);
                continue;
            }

            if (IsRetryableStatusCode(response.StatusCode) && attempt < MaxRetryAttempts)
            {
                retryCount++;
                TimeSpan retryDelay = CalculateRetryDelay(retryCount, response.Headers.RetryAfter);
                _logger.LogWarning(
                    "Provider returned retryable HTTP {StatusCode} on attempt {Attempt} of {MaxAttempts}. Retrying after {RetryDelayMilliseconds} ms.",
                    (int)response.StatusCode,
                    attempt + 1,
                    MaxRetryAttempts + 1,
                    Math.Round(retryDelay.TotalMilliseconds, MidpointRounding.AwayFromZero));
                await _delayAsync(retryDelay, cancellationToken);
                continue;
            }

            ThrowConversationRequestFailed(response.StatusCode, responseBody);
        }

        throw new ConversationProviderException(
            "Unable to complete the conversation request. The provider retry loop ended unexpectedly.");
    }

    private static HttpRequestMessage CreateHttpRequest(
        Uri baseUri,
        ProviderKind providerKind,
        string apiKey,
        string requestBody)
    {
        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, "chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (providerKind == ProviderKind.OpenRouter)
        {
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", OpenRouterApplicationUrl);
            httpRequest.Headers.TryAddWithoutValidation("X-Title", OpenRouterApplicationTitle);
        }

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private HttpRequestMessage CreateOpenAiChatGptAccountHttpRequest(
        Uri baseUri,
        OpenAiChatGptAccountResolvedCredential credential,
        string requestBody)
    {
        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, "responses"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        httpRequest.Headers.TryAddWithoutValidation("originator", "nanoagent");
        httpRequest.Headers.TryAddWithoutValidation("session_id", _sessionId);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", "NanoAgent/1.0");
        if (!string.IsNullOrWhiteSpace(credential.AccountId))
        {
            httpRequest.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credential.AccountId);
        }

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private static OpenAiChatCompletionRequest BuildRequestPayload(ConversationProviderRequest request)
    {
        List<OpenAiChatCompletionRequestMessage> messages = [];

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new OpenAiChatCompletionRequestMessage(
                "system",
                request.SystemPrompt.Trim()));
        }

        foreach (ConversationRequestMessage message in request.Messages)
        {
            messages.Add(MapMessage(message));
        }

        OpenAiChatCompletionToolDefinition[] tools = request.AvailableTools
            .Select(definition => new OpenAiChatCompletionToolDefinition(
                "function",
                new OpenAiChatCompletionFunctionDefinition(
                    definition.Name,
                    definition.Description,
                    definition.Schema)))
            .ToArray();

        // Intentionally omit max_tokens so the provider can use its maximum supported output/context policy.
        return new OpenAiChatCompletionRequest(
            request.ModelId,
            messages,
            tools,
            ReasoningEffortOptions.ToProviderValue(request.ReasoningEffort));
    }

    private static OpenAiResponsesRequest BuildResponsesRequestPayload(ConversationProviderRequest request)
    {
        List<OpenAiResponsesInputItem> input = [];

        foreach (ConversationRequestMessage message in request.Messages)
        {
            input.AddRange(MapResponsesMessage(message));
        }

        OpenAiResponsesToolDefinition[] tools = request.AvailableTools
            .Select(definition => new OpenAiResponsesToolDefinition(
                "function",
                definition.Name,
                definition.Description,
                NormalizeResponsesToolSchema(definition.Schema),
                Strict: true))
            .ToArray();

        string? reasoningEffort = ReasoningEffortOptions.ToProviderValue(request.ReasoningEffort);

        return new OpenAiResponsesRequest(
            request.ModelId,
            input,
            Stream: true,
            Store: false,
            string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt.Trim(),
            tools.Length == 0 ? null : tools,
            reasoningEffort is null ? null : new OpenAiResponsesReasoning(reasoningEffort, "auto"),
            reasoningEffort is null ? null : ["reasoning.encrypted_content"],
            ParallelToolCalls: true);
    }

    private static string NormalizeOpenAiChatGptAccountResponseBody(string responseBody)
    {
        return OpenAiResponsesEventStreamParser.TryParseResponsePayload(responseBody) ?? responseBody;
    }

    private static IReadOnlyList<OpenAiResponsesInputItem> MapResponsesMessage(
        ConversationRequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        List<OpenAiResponsesInputItem> items = [];

        if (string.Equals(message.Role, "tool", StringComparison.Ordinal))
        {
            items.Add(new OpenAiResponsesInputItem(
                Type: "function_call_output",
                CallId: message.ToolCallId,
                Output: message.Content));
            return items;
        }

        string contentType = string.Equals(message.Role, "assistant", StringComparison.Ordinal)
            ? "output_text"
            : "input_text";

        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            items.Add(new OpenAiResponsesInputItem(
                Role: message.Role,
                Content:
                [
                    new OpenAiResponsesContentPart(contentType, message.Content)
                ]));
        }

        foreach (ConversationToolCall toolCall in message.ToolCalls)
        {
            items.Add(new OpenAiResponsesInputItem(
                Type: "function_call",
                CallId: toolCall.Id,
                Name: toolCall.Name,
                Arguments: toolCall.ArgumentsJson));
        }

        return items;
    }

    private static JsonElement NormalizeResponsesToolSchema(JsonElement schema)
    {
        JsonNode? node = JsonNode.Parse(schema.GetRawText());
        if (node is null)
        {
            return schema.Clone();
        }

        NormalizeSchemaNode(node);
        using JsonDocument document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static void NormalizeSchemaNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            if (TryGetString(jsonObject["type"]) is "object")
            {
                jsonObject["additionalProperties"] = false;

                if (jsonObject["properties"] is JsonObject properties)
                {
                    JsonArray required = [];
                    foreach (KeyValuePair<string, JsonNode?> property in properties)
                    {
                        required.Add((JsonNode?)JsonValue.Create(property.Key));
                    }

                    jsonObject["required"] = required;
                }
            }

            foreach (KeyValuePair<string, JsonNode?> property in jsonObject.ToArray())
            {
                if (property.Value is not null)
                {
                    NormalizeSchemaNode(property.Value);
                }
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (JsonNode? item in jsonArray)
            {
                if (item is not null)
                {
                    NormalizeSchemaNode(item);
                }
            }
        }
    }

    private static string? TryGetString(JsonNode? node)
    {
        try
        {
            return node?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static OpenAiChatCompletionRequestMessage MapMessage(ConversationRequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ToolCalls.Count > 0)
        {
            OpenAiChatCompletionToolCall[] toolCalls = message.ToolCalls
                .Select(static toolCall => new OpenAiChatCompletionToolCall(
                    toolCall.Id,
                    "function",
                    new OpenAiChatCompletionFunctionCall(
                        toolCall.Name,
                        toolCall.ArgumentsJson)))
                .ToArray();

            return new OpenAiChatCompletionRequestMessage(
                message.Role,
                message.Content,
                null,
                toolCalls);
        }

        return new OpenAiChatCompletionRequestMessage(
            message.Role,
            message.Content,
            message.ToolCallId);
    }

    private static string? TryGetResponseId(HttpResponseMessage response)
    {
        return TryGetFirstHeaderValue(response, "x-request-id")
            ?? TryGetFirstHeaderValue(response, "request-id");
    }

    private static string? TryGetFirstHeaderValue(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        int numericStatusCode = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
            numericStatusCode is >= 500 and <= 599;
    }

    private TimeSpan CalculateRetryDelay(
        int retryCount,
        RetryConditionHeaderValue? retryAfter)
    {
        double exponentialMilliseconds = BaseRetryDelay.TotalMilliseconds *
            Math.Pow(2, Math.Max(0, retryCount - 1));
        TimeSpan exponentialDelay = TimeSpan.FromMilliseconds(
            Math.Min(exponentialMilliseconds, MaxRetryDelay.TotalMilliseconds));
        TimeSpan jitteredDelay = TimeSpan.FromMilliseconds(
            Math.Clamp(_nextJitter(), 0d, 1d) * exponentialDelay.TotalMilliseconds);
        TimeSpan? retryAfterDelay = GetRetryAfterDelay(retryAfter);

        return retryAfterDelay is { } serverDelay && serverDelay > jitteredDelay
            ? serverDelay
            : jitteredDelay;
    }

    private static TimeSpan? GetRetryAfterDelay(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            TimeSpan delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero
                ? delay
                : null;
        }

        return null;
    }

    private static void ThrowConversationRequestFailed(
        HttpStatusCode statusCode,
        string responseBody)
    {
        string detail = string.IsNullOrWhiteSpace(responseBody)
            ? $"Provider returned HTTP {(int)statusCode}."
            : $"Provider returned HTTP {(int)statusCode}: {Truncate(responseBody.Trim(), 200)}";

        throw new ConversationProviderException(
            $"Unable to complete the conversation request. {detail}");
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private void LogDebugApiRequest(
        HttpMethod method,
        Uri? requestUri,
        string requestBody)
    {
#if DEBUG
        _logger.LogInformation(
            "OpenAI-compatible chat API request {Method} {RequestUri}: {RequestBody}",
            method,
            requestUri,
            requestBody);
#endif
    }

    private void LogDebugApiResponse(
        System.Net.HttpStatusCode statusCode,
        string? responseId,
        string responseBody)
    {
#if DEBUG
        _logger.LogInformation(
            "OpenAI-compatible chat API response {StatusCode} {ResponseId}: {ResponseBody}",
            (int)statusCode,
            responseId ?? "(none)",
            responseBody);
#endif
    }
}
