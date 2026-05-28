using Microsoft.Extensions.Logging;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Net;
using System.Net.Http.Headers;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class ConversationProviderHttpExecutor : IConversationProviderHttpExecutor
{
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly Func<double> _nextJitter;

    public ConversationProviderHttpExecutor(
        HttpClient httpClient,
        ILogger logger,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<double>? nextJitter = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
        _nextJitter = nextJitter ?? Random.Shared.NextDouble;
    }

    public async Task<ConversationProviderPayload> ExecuteAsync(
        ProviderKind providerKind,
        string requestBody,
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken,
        Func<string, string>? normalizeResponseBody = null,
        Func<CancellationToken, Task<bool>>? refreshAuthorizationAsync = null)
    {
        int retryCount = 0;
        bool forcedRefreshAfterAuthFailure = false;

        for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
        {
            using HttpRequestMessage httpRequest = createRequest();
            LogDebugApiRequest(httpRequest.Method, httpRequest.RequestUri, requestBody);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            LogDebugApiResponse(response.StatusCode, TryGetResponseId(response), responseBody);

            if (response.IsSuccessStatusCode)
            {
                string normalizedResponseBody = normalizeResponseBody is null
                    ? responseBody
                    : normalizeResponseBody(responseBody);
                if (string.IsNullOrWhiteSpace(normalizedResponseBody))
                {
                    throw new ConversationProviderException(
                        "The provider returned an empty response body for the conversation request.");
                }

                return new ConversationProviderPayload(
                    providerKind,
                    normalizedResponseBody,
                    TryGetResponseId(response),
                    retryCount);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized &&
                !forcedRefreshAfterAuthFailure &&
                refreshAuthorizationAsync is not null &&
                await refreshAuthorizationAsync(cancellationToken))
            {
                forcedRefreshAfterAuthFailure = true;
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
        HttpStatusCode statusCode,
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
