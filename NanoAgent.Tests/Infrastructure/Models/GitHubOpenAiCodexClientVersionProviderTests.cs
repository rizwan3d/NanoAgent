using System.Net;
using System.Text;
using NanoAgent.Infrastructure.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace NanoAgent.Tests.Infrastructure.Models;

public sealed class GitHubOpenAiCodexClientVersionProviderTests
{
    [Fact]
    public async Task GetClientVersionAsync_Should_ParseLatestReleaseTagFromGitHub()
    {
        RecordingHandler handler = new("""
            {
              "tag_name": "rust-v0.125.0"
            }
            """);
        GitHubOpenAiCodexClientVersionProvider sut = CreateSut(handler);

        string version = await sut.GetClientVersionAsync(CancellationToken.None);

        version.Should().Be("0.125.0");
        handler.RequestUri.Should().Be(new Uri("https://api.github.com/repos/openai/codex/releases/latest"));
    }

    [Fact]
    public async Task GetClientVersionAsync_Should_ReturnFallbackVersion_When_GitHubRequestFails()
    {
        RecordingHandler handler = new("{}", HttpStatusCode.ServiceUnavailable);
        GitHubOpenAiCodexClientVersionProvider sut = CreateSut(handler);

        string version = await sut.GetClientVersionAsync(CancellationToken.None);

        version.Should().Be(GitHubOpenAiCodexClientVersionProvider.FallbackClientVersion);
    }

    private static GitHubOpenAiCodexClientVersionProvider CreateSut(RecordingHandler handler)
    {
        return new GitHubOpenAiCodexClientVersionProvider(
            new HttpClient(handler),
            NullLogger<GitHubOpenAiCodexClientVersionProvider>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public RecordingHandler(
            string responseBody,
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            HttpResponseMessage response = new(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
