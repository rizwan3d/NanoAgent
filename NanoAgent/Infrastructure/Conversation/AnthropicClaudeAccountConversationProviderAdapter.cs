using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Anthropic;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class AnthropicClaudeAccountConversationProviderAdapter : IConversationProviderAdapter
{
    private const string AnthropicClaudeAccountBetaHeader = "claude-code-20250219,oauth-2025-04-20";
    private const string AnthropicClaudeAccountUserAgent = "claude-cli/2.1.75";
    private const string AnthropicVersion = "2023-06-01";

    private readonly IConversationProviderHttpExecutor _httpExecutor;
    private readonly ConversationProviderRequestPayloadFactory _payloadFactory;
    private readonly ConversationProviderResponseNormalizer _responseNormalizer;
    private readonly IAnthropicClaudeAccountCredentialService? _credentialService;

    public AnthropicClaudeAccountConversationProviderAdapter(
        IConversationProviderHttpExecutor httpExecutor,
        ConversationProviderRequestPayloadFactory payloadFactory,
        ConversationProviderResponseNormalizer responseNormalizer,
        IAnthropicClaudeAccountCredentialService? credentialService)
    {
        _httpExecutor = httpExecutor;
        _payloadFactory = payloadFactory;
        _responseNormalizer = responseNormalizer;
        _credentialService = credentialService;
    }

    public async Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (_credentialService is null)
        {
            throw new ConversationProviderException(
                "Anthropic Claude Pro/Max credentials cannot be resolved in this runtime.");
        }

        AnthropicMessagesRequest payload = _payloadFactory.BuildAnthropicMessagesRequest(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.AnthropicMessagesRequest);
        Uri baseUri = request.ProviderProfile.ResolveBaseUri();
        AnthropicClaudeAccountResolvedCredential credential =
            await _credentialService.ResolveAsync(
                request.ApiKey,
                forceRefresh: false,
                cancellationToken);

        return await _httpExecutor.ExecuteAsync(
            request.ProviderProfile.ProviderKind,
            () => CreateHttpRequest(baseUri, credential, requestBody),
            cancellationToken,
            _responseNormalizer.ConvertAnthropicMessagesResponseToChatCompletion,
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
        AnthropicClaudeAccountResolvedCredential credential,
        string requestBody)
    {
        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, "messages"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        httpRequest.Headers.Accept.ParseAdd("application/json");
        httpRequest.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        httpRequest.Headers.TryAddWithoutValidation("anthropic-beta", AnthropicClaudeAccountBetaHeader);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", AnthropicClaudeAccountUserAgent);
        httpRequest.Headers.TryAddWithoutValidation("x-app", "cli");
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }
}
