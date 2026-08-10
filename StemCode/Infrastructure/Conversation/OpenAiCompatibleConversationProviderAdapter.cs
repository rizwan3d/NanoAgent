using StemCode.Application.Models;
using StemCode.Domain.Models;
using StemCode.Infrastructure.StemCodeEnterprise;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StemCode.Infrastructure.Conversation;

internal sealed class OpenAiCompatibleConversationProviderAdapter : IConversationProviderAdapter
{
    private const string OpenRouterApplicationUrl = "https://github.com/rizwan3d/StemCode";
    private const string KiloCodeEditorName = "StemCode";
    private const string KiloCodeUserAgent = "stemcode-kilo-provider";

    private readonly IConversationProviderHttpExecutor _httpExecutor;
    private readonly ConversationProviderRequestPayloadFactory _payloadFactory;
    private readonly IStemCodeEnterpriseCredentialService? _stemCodeEnterpriseCredentialService;
    private readonly ProviderRequestProjectHeaderProvider? _providerRequestProjectHeaderProvider;

    public OpenAiCompatibleConversationProviderAdapter(
        IConversationProviderHttpExecutor httpExecutor,
        ConversationProviderRequestPayloadFactory payloadFactory,
        IStemCodeEnterpriseCredentialService? stemCodeEnterpriseCredentialService = null,
        ProviderRequestProjectHeaderProvider? providerRequestProjectHeaderProvider = null)
    {
        _httpExecutor = httpExecutor;
        _payloadFactory = payloadFactory;
        _stemCodeEnterpriseCredentialService = stemCodeEnterpriseCredentialService;
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
        bool usesStemCodeEnterpriseCredentials = _stemCodeEnterpriseCredentialService?.CanResolve(request.ApiKey) == true;

        if (usesStemCodeEnterpriseCredentials)
        {
            IStemCodeEnterpriseCredentialService enterpriseCredentialService =
                _stemCodeEnterpriseCredentialService ??
                throw new InvalidOperationException("StemCode Enterprise credentials cannot be resolved in this runtime.");

            StemCodeEnterpriseResolvedCredential credential = await enterpriseCredentialService.ResolveAsync(
                request.ApiKey,
                forceRefresh: false,
                cancellationToken);
            authorizationValue = credential.AccessToken;

            return await _httpExecutor.ExecuteAsync(
                request.ProviderProfile.ProviderKind,
                () => CreateHttpRequest(
                    baseUri,
                    request.ProviderProfile.ProviderKind,
                    authorizationValue,
                    requestBody,
                    usesStemCodeEnterpriseCredentials),
                cancellationToken,
                refreshAuthorizationAsync: async token =>
                {
                    credential = await enterpriseCredentialService.ResolveAsync(
                        request.ApiKey,
                        forceRefresh: true,
                        token);
                    authorizationValue = credential.AccessToken;
                    return true;
                },
                onRetryAsync: request.OnProviderRetryAsync);
        }

        return await _httpExecutor.ExecuteAsync(
            request.ProviderProfile.ProviderKind,
            () => CreateHttpRequest(
                baseUri,
                request.ProviderProfile.ProviderKind,
                authorizationValue,
                requestBody,
                usesStemCodeEnterpriseCredentials),
            cancellationToken,
            onRetryAsync: request.OnProviderRetryAsync);
    }

    private HttpRequestMessage CreateHttpRequest(
        Uri baseUri,
        ProviderKind providerKind,
        string apiKey,
        string requestBody,
        bool usesStemCodeEnterpriseCredentials)
    {
        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, "chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        string requestTitle =
            ProviderRequestProjectHeaderProvider.GetConfiguredTitle() ??
            ProviderRequestProjectHeaderProvider.DefaultRequestTitle;
        if (providerKind == ProviderKind.OpenRouter)
        {
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", OpenRouterApplicationUrl);
            httpRequest.Headers.TryAddWithoutValidation("X-Title", requestTitle);
        }
        else if (providerKind == ProviderKind.KiloCode)
        {
            httpRequest.Headers.TryAddWithoutValidation("X-KILOCODE-EDITORNAME", KiloCodeEditorName);
            httpRequest.Headers.TryAddWithoutValidation("User-Agent", KiloCodeUserAgent);
        }

        string? configuredProjectName = ProviderRequestProjectHeaderProvider.GetConfiguredProjectName();
        if (usesStemCodeEnterpriseCredentials)
        {
            httpRequest.Headers.TryAddWithoutValidation("X-Title", requestTitle);
        }

        if (usesStemCodeEnterpriseCredentials || configuredProjectName is not null)
        {
            httpRequest.Headers.TryAddWithoutValidation(
                "X-Project",
                _providerRequestProjectHeaderProvider?.GetProjectName() ??
                configuredProjectName ??
                ProviderRequestProjectHeaderProvider.ResolveProjectName(Directory.GetCurrentDirectory()));
        }

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }
}
