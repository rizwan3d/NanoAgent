using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class OpenCodeZenConversationProviderAdapter : IConversationProviderAdapter
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly IConversationProviderHttpExecutor _httpExecutor;
    private readonly ConversationProviderRequestPayloadFactory _payloadFactory;
    private readonly ConversationProviderResponseNormalizer _responseNormalizer;

    public OpenCodeZenConversationProviderAdapter(
        IConversationProviderHttpExecutor httpExecutor,
        ConversationProviderRequestPayloadFactory payloadFactory,
        ConversationProviderResponseNormalizer responseNormalizer)
    {
        _httpExecutor = httpExecutor;
        _payloadFactory = payloadFactory;
        _responseNormalizer = responseNormalizer;
    }

    public bool CanHandle(ConversationProviderRequest request)
    {
        return request.ProviderProfile.ProviderKind == ProviderKind.OpenCodeZen &&
            ResolveEndpoint(request.ModelId) is not OpenCodeZenEndpoint.ChatCompletions;
    }

    public Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        OpenCodeZenEndpoint endpoint = ResolveEndpoint(request.ModelId);
        string requestBody = endpoint switch
        {
            OpenCodeZenEndpoint.Responses => JsonSerializer.Serialize(
                _payloadFactory.BuildResponsesRequest(request),
                OpenAiConversationJsonContext.Default.OpenAiResponsesRequest),
            OpenCodeZenEndpoint.Messages => JsonSerializer.Serialize(
                _payloadFactory.BuildAnthropicMessagesRequest(request),
                OpenAiConversationJsonContext.Default.AnthropicMessagesRequest),
            _ => throw new InvalidOperationException($"Unsupported OpenCode Zen endpoint '{endpoint}'.")
        };
        Uri baseUri = request.ProviderProfile.ResolveBaseUri();

        return _httpExecutor.ExecuteAsync(
            request.ProviderProfile.ProviderKind,
            requestBody,
            () => CreateHttpRequest(baseUri, endpoint, request.ApiKey, requestBody),
            cancellationToken,
            endpoint switch
            {
                OpenCodeZenEndpoint.Responses => _responseNormalizer.NormalizeOpenAiResponsesBody,
                OpenCodeZenEndpoint.Messages => _responseNormalizer.ConvertAnthropicMessagesResponseToChatCompletion,
                _ => null
            });
    }

    internal static OpenCodeZenEndpoint ResolveEndpoint(string modelId)
    {
        string normalizedModelId = modelId.Trim().ToLowerInvariant();
        if (normalizedModelId.StartsWith("gpt-", StringComparison.Ordinal))
        {
            return OpenCodeZenEndpoint.Responses;
        }

        if (normalizedModelId.StartsWith("claude-", StringComparison.Ordinal))
        {
            return OpenCodeZenEndpoint.Messages;
        }

        return OpenCodeZenEndpoint.ChatCompletions;
    }

    private static HttpRequestMessage CreateHttpRequest(
        Uri baseUri,
        OpenCodeZenEndpoint endpoint,
        string apiKey,
        string requestBody)
    {
        string path = endpoint switch
        {
            OpenCodeZenEndpoint.Responses => "responses",
            OpenCodeZenEndpoint.Messages => "messages",
            _ => throw new InvalidOperationException($"Unsupported OpenCode Zen endpoint '{endpoint}'.")
        };

        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, path));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Headers.Accept.ParseAdd("application/json");
        if (endpoint == OpenCodeZenEndpoint.Messages)
        {
            httpRequest.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        }

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }
}
