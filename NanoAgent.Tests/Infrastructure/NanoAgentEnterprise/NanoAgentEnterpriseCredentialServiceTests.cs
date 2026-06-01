using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.NanoAgentEnterprise;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.NanoAgentEnterprise;

public sealed class NanoAgentEnterpriseCredentialServiceTests
{
    [Fact]
    public async Task ResolveAsync_Should_RefreshExpiredCredentialsAndSaveUpdatedSecret()
    {
        string storedCredentials = JsonSerializer.Serialize(new
        {
            Type = "nanoagent-enterprise",
            ProviderBaseUrl = "https://enterprise.example.com/v1",
            AccessToken = "old-access",
            RefreshToken = "old-refresh",
            ExpiresAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds()
        });
        RecordingHandler handler = new("""
            {
              "access_token": "new-access",
              "token_type": "Bearer",
              "expires_in": 3600,
              "refresh_token": "new-refresh"
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

        NanoAgentEnterpriseCredentialService sut = new(
            httpClient,
            secretStore.Object,
            statusMessageWriter.Object,
            NullLogger<NanoAgentEnterpriseCredentialService>.Instance);

        NanoAgentEnterpriseResolvedCredential result = await sut.ResolveAsync(
            storedCredentials,
            forceRefresh: false,
            CancellationToken.None);

        result.AccessToken.Should().Be("new-access");
        handler.RequestUri.Should().Be(new Uri("https://enterprise.example.com/oauth/token"));
        handler.RequestBody.Should().Contain("grant_type=refresh_token");
        handler.RequestBody.Should().Contain("refresh_token=old-refresh");
        savedSecret.Should().NotBeNullOrWhiteSpace();
        sut.CanResolve(savedSecret!).Should().BeTrue();
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
