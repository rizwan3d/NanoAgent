using System.Net;
using System.Net.Http;
using System.Text;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

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

    private static OpenAiCompatibleModelProviderClient CreateSut(HttpClient httpClient)
    {
        return new OpenAiCompatibleModelProviderClient(
            httpClient,
            NullLogger<OpenAiCompatibleModelProviderClient>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
