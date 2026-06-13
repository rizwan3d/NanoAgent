using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.OpenAi;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class OpenAiChatGptAccountConversationProviderAdapter : IConversationProviderAdapter
{
    private readonly IConversationProviderHttpExecutor _httpExecutor;
    private readonly ConversationProviderRequestPayloadFactory _payloadFactory;
    private readonly ConversationProviderResponseNormalizer _responseNormalizer;
    private readonly IOpenAiChatGptAccountCredentialService? _credentialService;
    private readonly string _sessionId;

    public OpenAiChatGptAccountConversationProviderAdapter(
        IConversationProviderHttpExecutor httpExecutor,
        ConversationProviderRequestPayloadFactory payloadFactory,
        ConversationProviderResponseNormalizer responseNormalizer,
        IOpenAiChatGptAccountCredentialService? credentialService,
        string? sessionId = null)
    {
        _httpExecutor = httpExecutor;
        _payloadFactory = payloadFactory;
        _responseNormalizer = responseNormalizer;
        _credentialService = credentialService;
        _sessionId = sessionId ?? Guid.NewGuid().ToString("N");
    }

    public async Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (_credentialService is null)
        {
            throw new ConversationProviderException(
                "OpenAI ChatGPT Plus/Pro credentials cannot be resolved in this runtime.");
        }

        OpenAiResponsesRequest payload = _payloadFactory.BuildResponsesRequest(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.OpenAiResponsesRequest);
        Uri baseUri = request.ProviderProfile.ResolveBaseUri();
        OpenAiChatGptAccountResolvedCredential credential =
            await _credentialService.ResolveAsync(
                request.ApiKey,
                forceRefresh: false,
                cancellationToken);

        return await _httpExecutor.ExecuteAsync(
            request.ProviderProfile.ProviderKind,
            () => CreateHttpRequest(baseUri, credential, requestBody, _sessionId),
            cancellationToken,
            _responseNormalizer.NormalizeOpenAiResponsesBody,
            async token =>
            {
                credential = await _credentialService.ResolveAsync(
                    request.ApiKey,
                    forceRefresh: true,
                    token);
                return true;
            });
    }

    private static HttpRequestMessage CreateHttpRequest(
        Uri baseUri,
        OpenAiChatGptAccountResolvedCredential credential,
        string requestBody,
        string sessionId)
    {
        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, "responses"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        httpRequest.Headers.TryAddWithoutValidation("originator", "nanoagent");
        httpRequest.Headers.TryAddWithoutValidation("session_id", sessionId);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", "NanoAgent/1.0");
        if (!string.IsNullOrWhiteSpace(credential.AccountId))
        {
            httpRequest.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credential.AccountId);
        }

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }
}
