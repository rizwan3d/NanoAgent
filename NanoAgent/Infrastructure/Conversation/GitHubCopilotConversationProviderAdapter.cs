using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.GitHub;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class GitHubCopilotConversationProviderAdapter : IConversationProviderAdapter
{
    private readonly IConversationProviderHttpExecutor _httpExecutor;
    private readonly ConversationProviderRequestPayloadFactory _payloadFactory;
    private readonly IGitHubCopilotCredentialService? _credentialService;

    public GitHubCopilotConversationProviderAdapter(
        IConversationProviderHttpExecutor httpExecutor,
        ConversationProviderRequestPayloadFactory payloadFactory,
        IGitHubCopilotCredentialService? credentialService)
    {
        _httpExecutor = httpExecutor;
        _payloadFactory = payloadFactory;
        _credentialService = credentialService;
    }

    public bool CanHandle(ConversationProviderRequest request)
    {
        return request.ProviderProfile.ProviderKind == ProviderKind.GitHubCopilot;
    }

    public async Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (_credentialService is null)
        {
            throw new ConversationProviderException(
                "GitHub Copilot credentials cannot be resolved in this runtime.");
        }

        OpenAiChatCompletionRequest payload = _payloadFactory.BuildChatCompletionRequest(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.OpenAiChatCompletionRequest);
        GitHubCopilotResolvedCredential credential =
            await _credentialService.ResolveAsync(
                request.ApiKey,
                forceRefresh: false,
                cancellationToken);

        return await _httpExecutor.ExecuteAsync(
            request.ProviderProfile.ProviderKind,
            requestBody,
            () => CreateHttpRequest(credential, request, requestBody),
            cancellationToken,
            refreshAuthorizationAsync: async token =>
            {
                credential = await _credentialService.ResolveAsync(
                    request.ApiKey,
                    forceRefresh: true,
                    token);
                return true;
            });
    }

    private static HttpRequestMessage CreateHttpRequest(
        GitHubCopilotResolvedCredential credential,
        ConversationProviderRequest providerRequest,
        string requestBody)
    {
        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(credential.BaseUri, "chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        GitHubCopilotCredentialService.ApplyCopilotHeaders(httpRequest);
        httpRequest.Headers.TryAddWithoutValidation("Openai-Intent", "conversation-edits");
        httpRequest.Headers.TryAddWithoutValidation("X-Initiator", InferInitiator(providerRequest.Messages));
        if (providerRequest.Messages.Any(static message => message.Attachments.Any(static attachment => attachment.IsImage)))
        {
            httpRequest.Headers.TryAddWithoutValidation("Copilot-Vision-Request", "true");
        }

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private static string InferInitiator(IReadOnlyList<ConversationRequestMessage> messages)
    {
        ConversationRequestMessage? lastMessage = messages.Count == 0
            ? null
            : messages[^1];

        return lastMessage is not null &&
            !string.Equals(lastMessage.Role, "user", StringComparison.Ordinal)
                ? "agent"
                : "user";
    }
}
