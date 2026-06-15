using Microsoft.Extensions.Logging;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class ConversationProviderHttpExecutor : IConversationProviderHttpExecutor
{
    private const int MaxRetryAttempts = 10;
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
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken,
        Func<string, string>? normalizeResponseBody = null,
        Func<CancellationToken, Task<bool>>? refreshAuthorizationAsync = null,
        Func<ProviderRetryProgress, CancellationToken, Task>? onRetryAsync = null,
        Func<Stream, Func<string, CancellationToken, Task>?, CancellationToken, Task<StreamingResponseReadResult>>? readResponseBodyAsync = null,
        Func<string, CancellationToken, Task>? onAssistantMessageChunkAsync = null)
    {
        int retryCount = 0;
        bool forcedRefreshAfterAuthFailure = false;

        for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
        {
            using HttpRequestMessage httpRequest = createRequest();
            LogDebugApiRequest(httpRequest.Method, httpRequest.RequestUri);

            HttpResponseMessage? response;
            try
            {
                response = await _httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (HttpRequestException ex) when (IsTransientNetworkError(ex) && attempt < MaxRetryAttempts)
            {
                retryCount++;
                TimeSpan networkRetryDelay = CalculateRetryDelay(retryCount, null);
                _logger.LogWarning(
                    ex,
                    "Transient network error on attempt {Attempt} of {MaxAttempts}. Retrying after {RetryDelayMilliseconds} ms.",
                    attempt + 1,
                    MaxRetryAttempts + 1,
                    Math.Round(networkRetryDelay.TotalMilliseconds, MidpointRounding.AwayFromZero));
                await ReportRetryAsync(
                    onRetryAsync,
                    retryCount,
                    DescribeNetworkRetryReason(ex),
                    cancellationToken);
                await _delayAsync(networkRetryDelay, cancellationToken);
                continue;
            }

            using (response)
            {
            string responseBody;
            bool assistantMessageWasStreamed = false;
            if (response.IsSuccessStatusCode &&
                readResponseBodyAsync is not null)
            {
                await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                StreamingResponseReadResult readResult = await readResponseBodyAsync(
                    responseStream,
                    onAssistantMessageChunkAsync,
                    cancellationToken);
                responseBody = readResult.ResponseBody;
                assistantMessageWasStreamed = readResult.AssistantMessageWasStreamed;
            }
            else
            {
                responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            }

            LogDebugApiResponse(response.StatusCode, TryGetResponseId(response));

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
                    retryCount,
                    assistantMessageWasStreamed);
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
                await ReportRetryAsync(
                    onRetryAsync,
                    retryCount,
                    $"HTTP {(int)response.StatusCode}",
                    cancellationToken);
                await _delayAsync(retryDelay, cancellationToken);
                continue;
            }

            ThrowConversationRequestFailed(response.StatusCode, responseBody);
            } // using (response)
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

    private static bool IsTransientNetworkError(HttpRequestException ex)
    {
        SocketException? socketEx = ex.InnerException as SocketException
            ?? ex.InnerException?.InnerException as SocketException;

        if (socketEx is not null)
        {
            return socketEx.SocketErrorCode is
                SocketError.ConnectionReset or
                SocketError.ConnectionAborted or
                SocketError.TimedOut or
                SocketError.HostNotFound or
                SocketError.TryAgain or
                SocketError.HostUnreachable or
                SocketError.NetworkUnreachable;
        }

        return ex.InnerException is IOException;
    }

    private static string DescribeNetworkRetryReason(HttpRequestException ex)
    {
        SocketException? socketEx = ex.InnerException as SocketException
            ?? ex.InnerException?.InnerException as SocketException;

        return socketEx?.SocketErrorCode switch
        {
            SocketError.HostNotFound or SocketError.TryAgain => "host not found",
            SocketError.HostUnreachable => "host unreachable",
            SocketError.NetworkUnreachable => "network unreachable",
            SocketError.ConnectionReset => "connection reset",
            SocketError.ConnectionAborted => "connection aborted",
            SocketError.TimedOut => "connection timed out",
            _ => "network error"
        };
    }

    private static async Task ReportRetryAsync(
        Func<ProviderRetryProgress, CancellationToken, Task>? onRetryAsync,
        int attempt,
        string reason,
        CancellationToken cancellationToken)
    {
        if (onRetryAsync is null)
        {
            return;
        }

        try
        {
            await onRetryAsync(
                new ProviderRetryProgress(attempt, MaxRetryAttempts, reason),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Surfacing retry progress is best-effort: a misbehaving listener
            // must never abort the request that is otherwise about to retry.
        }
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

        if (retryAfterDelay is not { } serverDelay || serverDelay <= jitteredDelay)
        {
            return jitteredDelay;
        }

        // Honor the server's Retry-After hint, but never wait longer than the
        // configured ceiling so a hostile or misconfigured server cannot stall
        // the request for an arbitrarily long time.
        return serverDelay > MaxRetryDelay ? MaxRetryDelay : serverDelay;
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
        Uri? requestUri)
    {
#if DEBUG
        // Intentionally omits the request body: it can carry prompts, file
        // contents, and other sensitive material that must not reach logs.
        _logger.LogInformation(
            "OpenAI-compatible chat API request {Method} {RequestUri}",
            method,
            requestUri);
#endif
    }

    private void LogDebugApiResponse(
        HttpStatusCode statusCode,
        string? responseId)
    {
#if DEBUG
        // Intentionally omits the response body to avoid logging model output.
        _logger.LogInformation(
            "OpenAI-compatible chat API response {StatusCode} {ResponseId}",
            (int)statusCode,
            responseId ?? "(none)");
#endif
    }
}

internal readonly record struct StreamingResponseReadResult(
    string ResponseBody,
    bool AssistantMessageWasStreamed);
