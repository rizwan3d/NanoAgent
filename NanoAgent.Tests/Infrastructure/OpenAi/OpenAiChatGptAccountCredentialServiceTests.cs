using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.OpenAi;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.OpenAi;

public sealed class OpenAiChatGptAccountCredentialServiceTests
{
    [Fact]
    public async Task ResolveAsync_Should_RefreshExpiredCredentialsAndSaveUpdatedSecret()
    {
        OpenAiChatGptAccountCredentials expiredCredentials = new(
            "openai-chatgpt-account",
            "old-access",
            "old-refresh",
            DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds(),
            "user@example.com",
            "acct_old");
        string storedCredentials = JsonSerializer.Serialize(
            expiredCredentials,
            OpenAiChatGptAccountJsonContext.Default.OpenAiChatGptAccountCredentials);
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

        OpenAiChatGptAccountCredentialService sut = new(
            httpClient,
            secretStore.Object,
            statusMessageWriter.Object,
            NullLogger<OpenAiChatGptAccountCredentialService>.Instance);

        OpenAiChatGptAccountResolvedCredential result = await sut.ResolveAsync(
            storedCredentials,
            forceRefresh: false,
            CancellationToken.None);

        result.AccessToken.Should().Be("new-access");
        result.AccountId.Should().Be("acct_old");
        handler.RequestUri.Should().Be(new Uri("https://auth.openai.com/oauth/token"));
        handler.RequestBody.Should().Contain("grant_type=refresh_token");
        handler.RequestBody.Should().Contain("refresh_token=old-refresh");
        savedSecret.Should().NotBeNullOrWhiteSpace();

        OpenAiChatGptAccountCredentials savedCredentials = JsonSerializer.Deserialize(
            savedSecret!,
            OpenAiChatGptAccountJsonContext.Default.OpenAiChatGptAccountCredentials)!;
        savedCredentials.AccessToken.Should().Be("new-access");
        savedCredentials.RefreshToken.Should().Be("new-refresh");
        savedCredentials.AccountId.Should().Be("acct_old");
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

