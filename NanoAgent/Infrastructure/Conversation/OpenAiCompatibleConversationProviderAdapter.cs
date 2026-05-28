using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class OpenAiCompatibleConversationProviderAdapter : IConversationProviderAdapter
{
    private const string OpenRouterApplicationTitle = "NanoAgent";
    private const string OpenRouterApplicationUrl = "https://github.com/rizwan3d/NanoAgent";
    private const string KiloCodeEditorName = "NanoAgent";
    private const string KiloCodeUserAgent = "nanoagent-kilo-provider";

    private readonly IConversationProviderHttpExecutor _httpExecutor;
    private readonly ConversationProviderRequestPayloadFactory _payloadFactory;

    public OpenAiCompatibleConversationProviderAdapter(
        IConversationProviderHttpExecutor httpExecutor,
        ConversationProviderRequestPayloadFactory payloadFactory)
    {
        _httpExecutor = httpExecutor;
        _payloadFactory = payloadFactory;
    }

    public Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        OpenAiChatCompletionRequest payload = _payloadFactory.BuildChatCompletionRequest(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.OpenAiChatCompletionRequest);
        Uri baseUri = request.ProviderProfile.ResolveBaseUri();

        return _httpExecutor.ExecuteAsync(
            request.ProviderProfile.ProviderKind,
            requestBody,
            () => CreateHttpRequest(baseUri, request.ProviderProfile.ProviderKind, request.ApiKey, requestBody),
            cancellationToken);
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
        else if (providerKind == ProviderKind.KiloCode)
        {
            httpRequest.Headers.TryAddWithoutValidation("X-KILOCODE-EDITORNAME", KiloCodeEditorName);
            httpRequest.Headers.TryAddWithoutValidation("User-Agent", KiloCodeUserAgent);
        }

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }
}
