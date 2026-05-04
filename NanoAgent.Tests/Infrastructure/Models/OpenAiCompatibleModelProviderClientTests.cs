using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoAgent.Application.Exceptions;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Anthropic;
using NanoAgent.Infrastructure.GitHub;
using NanoAgent.Infrastructure.Models;
using NanoAgent.Infrastructure.OpenAi;
using System.Net;
using System.Text;

namespace NanoAgent.Tests.Infrastructure.Models;

public sealed class OpenAiCompatibleModelProviderClientTests
{
    [Fact]
    public async Task GetAvailableModelsAsync_Should_RequestV1Models_When_CompatibleProviderBaseUrlHasNoPath()
    {
        RecordingHandler handler = new("""
            {
              "data": [
                { "id": "gpt-4.1" }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(httpClient);

        IReadOnlyList<AvailableModel> models = await sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "http://127.0.0.1:1234"),
            "test-key",
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("http://127.0.0.1:1234/v1/models"));
        models.Select(model => model.Id).Should().Equal("gpt-4.1");
    }

    [Fact]
    public async Task GetAvailableModelsAsync_Should_ParseContextWindowMetadata_When_Returned()
    {
        RecordingHandler handler = new("""
            {
              "data": [
                { "id": "model-a", "context_length": 128000 },
                { "id": "model-b", "inputTokenLimit": 1048576 },
                { "id": "model-c", "limits": { "context_window_tokens": "32768" } },
                { "id": "model-d", "context_length": -1 }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(httpClient);

        IReadOnlyList<AvailableModel> models = await sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "http://127.0.0.1:1234"),
            "test-key",
            CancellationToken.None);

        models.Select(model => model.ContextWindowTokens)
            .Should()
            .Equal(new int?[] { 128_000, 1_048_576, 32_768, null });
    }

    [Fact]
    public async Task GetAvailableModelsAsync_Should_RequestModelsRelativeToExplicitV1BaseUrl_When_CompatibleProviderBaseUrlAlreadyIncludesV1()
    {
        RecordingHandler handler = new("""
            {
              "data": [
                { "id": "gpt-5-mini" }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(httpClient);

        IReadOnlyList<AvailableModel> models = await sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "test-key",
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://provider.example.com/v1/models"));
        models.Select(model => model.Id).Should().Equal("gpt-5-mini");
    }

    [Fact]
    public async Task GetAvailableModelsAsync_Should_RequestGoogleAiStudioModelsEndpoint_When_GoogleAiStudioProviderIsConfigured()
    {
        RecordingHandler handler = new("""
            {
              "data": [
                { "id": "gemini-2.5-flash" }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(httpClient);

        IReadOnlyList<AvailableModel> models = await sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.GoogleAiStudio, null),
            "test-key",
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://generativelanguage.googleapis.com/v1beta/openai/models"));
        models.Select(model => model.Id).Should().Equal("gemini-2.5-flash");
    }

    [Fact]
    public async Task GetAvailableModelsAsync_Should_RequestOpenRouterModelsEndpointWithAppHeaders_When_OpenRouterProviderIsConfigured()
    {
        RecordingHandler handler = new("""
            {
              "data": [
                { "id": "openai/gpt-4o" }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(httpClient);

        IReadOnlyList<AvailableModel> models = await sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.OpenRouter, null),
            "test-key",
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://openrouter.ai/api/v1/models"));
        handler.AuthorizationHeader.Should().Be("Bearer test-key");
        handler.OpenRouterRefererHeader.Should().Be("https://github.com/rizwan3d/NanoAgent");
        handler.OpenRouterTitleHeader.Should().Be("NanoAgent");
        models.Select(model => model.Id).Should().Equal("openai/gpt-4o");
    }

    [Fact]
    public async Task GetAvailableModelsAsync_Should_RequestAnthropicModelsEndpointWithAnthropicHeaders_When_AnthropicProviderIsConfigured()
    {
        RecordingHandler handler = new("""
            {
              "data": [
                { "id": "claude-sonnet-4-6" }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(httpClient);

        IReadOnlyList<AvailableModel> models = await sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.Anthropic, null),
            "test-key",
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://api.anthropic.com/v1/models"));
        handler.AuthorizationHeader.Should().BeNull();
        handler.AnthropicApiKeyHeader.Should().Be("test-key");
        handler.AnthropicVersionHeader.Should().Be("2023-06-01");
        models.Select(model => model.Id).Should().Equal("claude-sonnet-4-6");
    }

    [Fact]
    public async Task GetAvailableModelsAsync_Should_RequestAnthropicClaudeAccountModelsEndpointWithOAuthHeaders_When_AccountProviderIsConfigured()
    {
        Mock<IAnthropicClaudeAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnthropicClaudeAccountResolvedCredential("access-token"));

        RecordingHandler handler = new("""
            {
              "data": [
                { "id": "claude-sonnet-4-6" }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(
            httpClient,
            anthropicClaudeAccountCredentialService: credentialService.Object);

        IReadOnlyList<AvailableModel> models = await sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.AnthropicClaudeAccount, null),
            "stored-credentials",
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://api.anthropic.com/v1/models"));
        handler.AuthorizationHeader.Should().Be("Bearer access-token");
        handler.AnthropicVersionHeader.Should().Be("2023-06-01");
        handler.AnthropicBetaHeader.Should().Contain("oauth-2025-04-20");
        handler.AnthropicAppHeader.Should().Be("cli");
        models.Select(model => model.Id).Should().Equal("claude-sonnet-4-6");
        credentialService.VerifyAll();
    }

    [Fact]
    public async Task GetAvailableModelsAsync_Should_RequestGitHubCopilotModelsEndpointWithCopilotHeaders_When_ProviderIsConfigured()
    {
        Mock<IGitHubCopilotCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubCopilotResolvedCredential(
                "copilot-token",
                EnterpriseDomain: null,
                new Uri("https://api.individual.githubcopilot.com/")));

        RecordingHandler handler = new("""
            {
              "data": [
                { "id": "gpt-5" }
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(
            httpClient,
            gitHubCopilotCredentialService: credentialService.Object);

        IReadOnlyList<AvailableModel> models = await sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.GitHubCopilot, null),
            "stored-credentials",
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://api.individual.githubcopilot.com/models"));
        handler.AuthorizationHeader.Should().Be("Bearer copilot-token");
        handler.CopilotIntegrationIdHeader.Should().Be("vscode-chat");
        models.Select(model => model.Id).Should().Equal("gpt-5");
        credentialService.VerifyAll();
    }



    [Fact]
    public async Task GetAvailableModelsAsync_Should_RequestAccountBackedModels_When_OpenAiChatGptAccountProviderIsConfigured()
    {
        Mock<IOpenAiChatGptAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("access-token", "acct_123"));

        RecordingHandler handler = new("""
            {
              "models": [
                { "slug": "account-model-a" },
                "account-model-b"
              ]
            }
            """);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(
            httpClient,
            credentialService.Object,
            new StubOpenAiCodexClientVersionProvider("0.125.0"));

        IReadOnlyList<AvailableModel> models = await sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.OpenAiChatGptAccount, null),
            "stored-credentials",
            CancellationToken.None);

        handler.RequestUri!.AbsolutePath.Should().EndWith("/models");
        handler.AuthorizationHeader.Should().Be("Bearer access-token");
        handler.AccountHeader.Should().Be("acct_123");
        models.Select(model => model.Id).Should().Equal("account-model-a", "account-model-b");
        handler.RequestUri!.Query.Should().Be("?client_version=0.125.0");
        credentialService.VerifyAll();
    }

    [Fact]
    public async Task GetAvailableModelsAsync_Should_RefreshCredentialsOnce_When_AccountModelRequestIsUnauthorized()
    {
        Mock<IOpenAiChatGptAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("expired-token", "acct_123"));
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("fresh-token", "acct_123"));

        SequencedHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "data": [
                        { "id": "fresh-model" }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(httpClient, credentialService.Object);

        IReadOnlyList<AvailableModel> models = await sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.OpenAiChatGptAccount, null),
            "stored-credentials",
            CancellationToken.None);

        handler.AuthorizationHeaders.Should().Equal("Bearer expired-token", "Bearer fresh-token");
        models.Select(model => model.Id).Should().Equal("fresh-model");
        credentialService.VerifyAll();
    }

    [Fact]
    public async Task GetAvailableModelsAsync_Should_ThrowModelProviderException_When_AccountModelRequestFails()
    {
        Mock<IOpenAiChatGptAccountCredentialService> credentialService = new(MockBehavior.Strict);
        credentialService
            .Setup(service => service.ResolveAsync(
                "stored-credentials",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiChatGptAccountResolvedCredential("access-token", "acct_123"));

        RecordingHandler handler = new(
            responseBody: string.Empty,
            statusCode: HttpStatusCode.ServiceUnavailable);
        HttpClient httpClient = new(handler);
        OpenAiCompatibleModelProviderClient sut = CreateSut(httpClient, credentialService.Object);

        Func<Task> action = () => sut.GetAvailableModelsAsync(
            new AgentProviderProfile(ProviderKind.OpenAiChatGptAccount, null),
            "stored-credentials",
            CancellationToken.None);

        await action.Should().ThrowAsync<ModelProviderException>()
            .WithMessage("*account API*HTTP 503*");
        credentialService.VerifyAll();
    }

    private static OpenAiCompatibleModelProviderClient CreateSut(
        HttpClient httpClient,
        IOpenAiChatGptAccountCredentialService? credentialService = null,
        IOpenAiCodexClientVersionProvider? versionProvider = null,
        IAnthropicClaudeAccountCredentialService? anthropicClaudeAccountCredentialService = null,
        IGitHubCopilotCredentialService? gitHubCopilotCredentialService = null)
    {
        return new OpenAiCompatibleModelProviderClient(
            httpClient,
            NullLogger<OpenAiCompatibleModelProviderClient>.Instance,
            credentialService,
            versionProvider,
            anthropicClaudeAccountCredentialService,
            gitHubCopilotCredentialService);
    }

    private sealed class StubOpenAiCodexClientVersionProvider : IOpenAiCodexClientVersionProvider
    {
        private readonly string _clientVersion;

        public StubOpenAiCodexClientVersionProvider(string clientVersion)
        {
            _clientVersion = clientVersion;
        }

        public Task<string> GetClientVersionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_clientVersion);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public Uri? RequestUri { get; private set; }

        public string? AuthorizationHeader { get; private set; }

        public string? AnthropicApiKeyHeader { get; private set; }

        public string? AnthropicVersionHeader { get; private set; }

        public string? AnthropicBetaHeader { get; private set; }

        public string? AnthropicAppHeader { get; private set; }

        public string? OpenRouterRefererHeader { get; private set; }

        public string? OpenRouterTitleHeader { get; private set; }

        public string? AccountHeader { get; private set; }

        public string? CopilotIntegrationIdHeader { get; private set; }

        public HttpStatusCode StatusCode { get; }

        public RecordingHandler(
            string responseBody,
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            StatusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CaptureRequest(request);

            HttpResponseMessage response = new(StatusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }

        private void CaptureRequest(HttpRequestMessage request)
        {
            RequestUri = request.RequestUri;
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            AnthropicApiKeyHeader = request.Headers.TryGetValues("x-api-key", out IEnumerable<string>? apiKeyValues)
                ? apiKeyValues.FirstOrDefault()
                : null;
            AnthropicVersionHeader = request.Headers.TryGetValues("anthropic-version", out IEnumerable<string>? versionValues)
                ? versionValues.FirstOrDefault()
                : null;
            AnthropicBetaHeader = request.Headers.TryGetValues("anthropic-beta", out IEnumerable<string>? betaValues)
                ? betaValues.FirstOrDefault()
                : null;
            AnthropicAppHeader = request.Headers.TryGetValues("x-app", out IEnumerable<string>? appValues)
                ? appValues.FirstOrDefault()
                : null;
            OpenRouterRefererHeader = request.Headers.TryGetValues("HTTP-Referer", out IEnumerable<string>? refererValues)
                ? refererValues.FirstOrDefault()
                : null;
            OpenRouterTitleHeader = request.Headers.TryGetValues("X-Title", out IEnumerable<string>? titleValues)
                ? titleValues.FirstOrDefault()
                : null;
            AccountHeader = request.Headers.TryGetValues(
                "Chat" + "G" + "P" + "T-Account-Id",
                out IEnumerable<string>? accountValues)
                ? accountValues.FirstOrDefault()
                : null;
            CopilotIntegrationIdHeader = request.Headers.TryGetValues(
                "Copilot-Integration-Id",
                out IEnumerable<string>? integrationValues)
                    ? integrationValues.FirstOrDefault()
                    : null;
        }
    }

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequencedHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<string?> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
