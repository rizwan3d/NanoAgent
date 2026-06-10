using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure;
using NanoAgent.Infrastructure.NanoAgentEnterprise;
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
    private readonly INanoAgentEnterpriseCredentialService? _nanoAgentEnterpriseCredentialService;
    private readonly ProviderRequestProjectHeaderProvider? _providerRequestProjectHeaderProvider;

    public OpenAiCompatibleConversationProviderAdapter(
        IConversationProviderHttpExecutor httpExecutor,
        ConversationProviderRequestPayloadFactory payloadFactory,
        INanoAgentEnterpriseCredentialService? nanoAgentEnterpriseCredentialService = null,
        ProviderRequestProjectHeaderProvider? providerRequestProjectHeaderProvider = null)
    {
        _httpExecutor = httpExecutor;
        _payloadFactory = payloadFactory;
        _nanoAgentEnterpriseCredentialService = nanoAgentEnterpriseCredentialService;
        _providerRequestProjectHeaderProvider = providerRequestProjectHeaderProvider;
    }

    public async Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        OpenAiChatCompletionRequest payload = _payloadFactory.BuildChatCompletionRequest(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.OpenAiChatCompletionRequest);
        Uri baseUri = request.ProviderProfile.ResolveBaseUri();
        string authorizationValue = request.ApiKey;
        bool usesNanoAgentEnterpriseCredentials = _nanoAgentEnterpriseCredentialService?.CanResolve(request.ApiKey) == true;

        if (usesNanoAgentEnterpriseCredentials)
        {
            INanoAgentEnterpriseCredentialService enterpriseCredentialService =
                _nanoAgentEnterpriseCredentialService ??
                throw new InvalidOperationException("NanoAgent Enterprise credentials cannot be resolved in this runtime.");

            NanoAgentEnterpriseResolvedCredential credential = await enterpriseCredentialService.ResolveAsync(
                request.ApiKey,
                forceRefresh: false,
                cancellationToken);
            authorizationValue = credential.AccessToken;

            return await _httpExecutor.ExecuteAsync(
                request.ProviderProfile.ProviderKind,
                requestBody,
                () => CreateHttpRequest(
                    baseUri,
                    request.ProviderProfile.ProviderKind,
                    authorizationValue,
                    requestBody,
                    usesNanoAgentEnterpriseCredentials),
                cancellationToken,
                refreshAuthorizationAsync: async token =>
                {
                    credential = await enterpriseCredentialService.ResolveAsync(
                        request.ApiKey,
                        forceRefresh: true,
                        token);
                    authorizationValue = credential.AccessToken;
                    return true;
                });
        }

        return await _httpExecutor.ExecuteAsync(
            request.ProviderProfile.ProviderKind,
            requestBody,
            () => CreateHttpRequest(
                baseUri,
                request.ProviderProfile.ProviderKind,
                authorizationValue,
                requestBody,
                usesNanoAgentEnterpriseCredentials),
            cancellationToken);
    }

    private HttpRequestMessage CreateHttpRequest(
        Uri baseUri,
        ProviderKind providerKind,
        string apiKey,
        string requestBody,
        bool usesNanoAgentEnterpriseCredentials)
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
        else if (usesNanoAgentEnterpriseCredentials)
        {
            httpRequest.Headers.TryAddWithoutValidation("X-Title", OpenRouterApplicationTitle);
            httpRequest.Headers.TryAddWithoutValidation(
                "X-Project",
                _providerRequestProjectHeaderProvider?.GetProjectName() ??
                ProviderRequestProjectHeaderProvider.ResolveProjectName(Directory.GetCurrentDirectory()));
        }

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }
}
