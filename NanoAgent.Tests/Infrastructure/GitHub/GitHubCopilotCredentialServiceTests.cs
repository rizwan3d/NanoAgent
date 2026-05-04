using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.GitHub;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.GitHub;

public sealed class GitHubCopilotCredentialServiceTests
{
    [Fact]
    public async Task ResolveAsync_Should_RefreshExpiredCredentialsAndSaveUpdatedSecret()
    {
        GitHubCopilotCredentials expiredCredentials = new(
            "github-copilot",
            "old-access",
            "github-refresh",
            DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds(),
            "ghe.example.com",
            "https://copilot-api.ghe.example.com");
        string storedCredentials = JsonSerializer.Serialize(
            expiredCredentials,
            GitHubCopilotJsonContext.Default.GitHubCopilotCredentials);
        long expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        RecordingHandler handler = new(
            $$"""
            {
              "token": "tid=abc;exp={{expiresAt}};proxy-ep=proxy.enterprise.githubcopilot.com;",
              "expires_at": {{expiresAt}}
            }
            """);
        HttpClient httpClient = new(handler);

        string? savedSecret = null;
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.SaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((value, _) => savedSecret = value)
            .Returns(Task.CompletedTask);

        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);

        GitHubCopilotCredentialService sut = new(
            httpClient,
            secretStore.Object,
            textPrompt.Object,
            statusMessageWriter.Object,
            NullLogger<GitHubCopilotCredentialService>.Instance);

        GitHubCopilotResolvedCredential result = await sut.ResolveAsync(
            storedCredentials,
            forceRefresh: false,
            CancellationToken.None);

        result.AccessToken.Should().Contain("proxy-ep=proxy.enterprise.githubcopilot.com");
        result.EnterpriseDomain.Should().Be("ghe.example.com");
        result.BaseUri.Should().Be(new Uri("https://api.enterprise.githubcopilot.com/"));
        handler.RequestUri.Should().Be(new Uri("https://api.ghe.example.com/copilot_internal/v2/token"));
        handler.AuthorizationHeader.Should().Be("Bearer github-refresh");
        handler.CopilotIntegrationIdHeader.Should().Be("vscode-chat");
        savedSecret.Should().NotBeNullOrWhiteSpace();

        GitHubCopilotCredentials savedCredentials = JsonSerializer.Deserialize(
            savedSecret!,
            GitHubCopilotJsonContext.Default.GitHubCopilotCredentials)!;
        savedCredentials.AccessToken.Should().Contain("proxy-ep=proxy.enterprise.githubcopilot.com");
        savedCredentials.RefreshToken.Should().Be("github-refresh");
        savedCredentials.BaseUrl.Should().Be("https://api.enterprise.githubcopilot.com");
        secretStore.VerifyAll();
        textPrompt.VerifyNoOtherCalls();
        statusMessageWriter.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("github.example.com", "github.example.com")]
    [InlineData("https://github.example.com/login", "github.example.com")]
    public void NormalizeDomain_Should_NormalizeGitHubEnterpriseInput(string input, string? expected)
    {
        GitHubCopilotCredentialService.NormalizeDomain(input).Should().Be(expected);
    }

    [Fact]
    public void GetBaseUrlFromToken_Should_FallbackToEnterpriseCopilotApi_When_ProxyEndpointIsMissing()
    {
        string result = GitHubCopilotCredentialService.GetBaseUrlFromToken(
            "token-without-proxy-endpoint",
            "ghe.example.com");

        result.Should().Be("https://copilot-api.ghe.example.com");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public string? AuthorizationHeader { get; private set; }

        public string? CopilotIntegrationIdHeader { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            CopilotIntegrationIdHeader = request.Headers.TryGetValues(
                "Copilot-Integration-Id",
                out IEnumerable<string>? integrationValues)
                    ? integrationValues.FirstOrDefault()
                    : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
