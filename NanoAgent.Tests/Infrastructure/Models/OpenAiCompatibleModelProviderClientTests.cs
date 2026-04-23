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

        public string? AuthorizationHeader { get; private set; }

        public string? AnthropicApiKeyHeader { get; private set; }

        public string? AnthropicVersionHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            AnthropicApiKeyHeader = request.Headers.TryGetValues("x-api-key", out IEnumerable<string>? apiKeyValues)
                ? apiKeyValues.FirstOrDefault()
                : null;
            AnthropicVersionHeader = request.Headers.TryGetValues("anthropic-version", out IEnumerable<string>? versionValues)
                ? versionValues.FirstOrDefault()
                : null;

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
