using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.Anthropic;

public sealed class AnthropicClaudeAccountCredentialServiceTests
{
    [Fact]
    public async Task ResolveAsync_Should_RefreshExpiredCredentialsAndSaveUpdatedSecret()
    {
        AnthropicClaudeAccountCredentials expiredCredentials = new(
            "anthropic-claude-account",
            "old-access",
            "old-refresh",
            DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds());
        string storedCredentials = JsonSerializer.Serialize(
            expiredCredentials,
            AnthropicClaudeAccountJsonContext.Default.AnthropicClaudeAccountCredentials);
        RecordingHandler handler = new("""
            {
              "access_token": "new-access",
              "refresh_token": "new-refresh",
              "expires_in": 3600
            }
            """);
        HttpClient httpClient = new(handler);

        string? savedSecret = null;
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.SaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((value, _) => savedSecret = value)
            .Returns(Task.CompletedTask);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);

        AnthropicClaudeAccountCredentialService sut = new(
            httpClient,
            secretStore.Object,
            statusMessageWriter.Object,
            NullLogger<AnthropicClaudeAccountCredentialService>.Instance);

        AnthropicClaudeAccountResolvedCredential result = await sut.ResolveAsync(
            storedCredentials,
            forceRefresh: false,
            CancellationToken.None);

        result.AccessToken.Should().Be("new-access");
        handler.RequestUri.Should().Be(new Uri("https://platform.claude.com/v1/oauth/token"));
        handler.RequestBody.Should().Contain("\"grant_type\":\"refresh_token\"");
        handler.RequestBody.Should().Contain("\"refresh_token\":\"old-refresh\"");
        savedSecret.Should().NotBeNullOrWhiteSpace();

        AnthropicClaudeAccountCredentials savedCredentials = JsonSerializer.Deserialize(
            savedSecret!,
            AnthropicClaudeAccountJsonContext.Default.AnthropicClaudeAccountCredentials)!;
        savedCredentials.AccessToken.Should().Be("new-access");
        savedCredentials.RefreshToken.Should().Be("new-refresh");
        secretStore.VerifyAll();
        statusMessageWriter.VerifyNoOtherCalls();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public string? RequestBody { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
