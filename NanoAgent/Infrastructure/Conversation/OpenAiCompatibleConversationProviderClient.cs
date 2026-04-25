using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using Microsoft.Extensions.Logging;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class OpenAiCompatibleConversationProviderClient : IConversationProviderClient
{
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly HttpClient _httpClient;
    private readonly Func<double> _nextJitter;
    private readonly ILogger<OpenAiCompatibleConversationProviderClient> _logger;

    public OpenAiCompatibleConversationProviderClient(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleConversationProviderClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<double>? nextJitter = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
        _nextJitter = nextJitter ?? Random.Shared.NextDouble;
    }

    public async Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        OpenAiChatCompletionRequest payload = BuildRequestPayload(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.OpenAiChatCompletionRequest);

        Uri baseUri = request.ProviderProfile.ResolveBaseUri();
        int retryCount = 0;

        for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
        {
            using HttpRequestMessage httpRequest = CreateHttpRequest(baseUri, request.ApiKey, requestBody);
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

    private static HttpRequestMessage CreateHttpRequest(
        Uri baseUri,
        string apiKey,
        string requestBody)
    {
        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, "chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
