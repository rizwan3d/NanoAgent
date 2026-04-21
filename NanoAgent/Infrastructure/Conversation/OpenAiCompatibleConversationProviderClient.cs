using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.OpenAi;
using Microsoft.Extensions.Logging;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class OpenAiCompatibleConversationProviderClient : IConversationProviderClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiCompatibleConversationProviderClient> _logger;

    public OpenAiCompatibleConversationProviderClient(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleConversationProviderClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Uri baseUri = OpenAiBaseUriResolver.Resolve(request.ProviderProfile);
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, "chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);

        OpenAiChatCompletionRequest payload = BuildRequestPayload(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.OpenAiChatCompletionRequest);

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        LogDebugApiRequest(httpRequest.Method, httpRequest.RequestUri, requestBody);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        LogDebugApiResponse(response.StatusCode, TryGetResponseId(response), responseBody);

        if (!response.IsSuccessStatusCode)
        {
            string detail = string.IsNullOrWhiteSpace(responseBody)
                ? $"Provider returned HTTP {(int)response.StatusCode}."
                : $"Provider returned HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 200)}";

            throw new ConversationProviderException(
                $"Unable to complete the conversation request. {detail}");
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new ConversationProviderException(
                "The provider returned an empty response body for the conversation request.");
        }

        return new ConversationProviderPayload(
            request.ProviderProfile.ProviderKind,
            responseBody,
            TryGetResponseId(response));
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
            tools);
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
        if (response.Headers.TryGetValues("x-request-id", out IEnumerable<string>? requestIds))
        {
            return requestIds.FirstOrDefault();
        }

        return null;
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
