using FluentAssertions;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.Tools;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.Tools;

public sealed class WebSearchServiceTests
{
    [Fact]
    public async Task RunAsync_Should_CallExaWebSearch_AndParseResults()
    {
        RecordingHandler handler = new(
            """
            Title: .NET documentation
            URL: https://learn.microsoft.com/en-us/dotnet/
            Published: 2024-01-01
            Author: Microsoft
            Highlights:
            Learn to use .NET on any platform.
            ---
            Title: .NET home
            URL: https://dotnet.microsoft.com/
            Published: N/A
            Author: N/A
            Highlights:
            Official site.
            """);

        HttpClient httpClient = new(handler);
        WebSearchService sut = new(httpClient);

        WebSearchResult result = await sut.RunAsync(
            new WebSearchRequest("medium", [new WebSearchQuery("dotnet", 2)]),
            "session_1",
            CancellationToken.None);

        handler.Methods.Should().Contain("initialize");
        handler.Methods.Should().Contain("tools/call");
        handler.LastToolCall.Should().Contain("web_search_exa");
        handler.LastToolCall.Should().Contain("dotnet");
        handler.LastToolCall.Should().Contain("\"numResults\":2");

        result.SearchQuery.Should().ContainSingle();
        WebSearchQueryResult search = result.SearchQuery[0];
        search.Query.Should().Be("dotnet");
        search.Content.Should().Contain("Learn to use .NET on any platform.");
        search.Results.Should().HaveCount(2);
        search.Results[0].Title.Should().Be(".NET documentation");
        search.Results[0].Url.Should().Be("https://learn.microsoft.com/en-us/dotnet/");
        search.Results[0].Published.Should().Be("2024-01-01");
        search.Results[0].Author.Should().Be("Microsoft");
        search.Results[1].Url.Should().Be("https://dotnet.microsoft.com/");
        search.Results[1].Published.Should().BeNull();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_Should_ForwardExaApiKey_FromEnvironment_When_Present()
    {
        string? original = Environment.GetEnvironmentVariable("EXA_API_KEY");
        Environment.SetEnvironmentVariable("EXA_API_KEY", "secret-key value");
        try
        {
            RecordingHandler handler = new("Title: x\nURL: https://example.test/");
            HttpClient httpClient = new(handler);
            WebSearchService sut = new(httpClient);

            await sut.RunAsync(
                new WebSearchRequest("short", [new WebSearchQuery("dotnet")]),
                "session_1",
                CancellationToken.None);

            handler.RequestUris.Should().NotBeEmpty();
            handler.RequestUris.Should().OnlyContain(uri =>
                uri.Query.Contains("exaApiKey=secret-key%20value"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("EXA_API_KEY", original);
        }
    }

    [Fact]
    public async Task RunAsync_Should_UseAnonymousEndpoint_When_NoApiKeyPresent()
    {
        string? originalUpper = Environment.GetEnvironmentVariable("EXA_API_KEY");
        string? originalCamel = Environment.GetEnvironmentVariable("exaApiKey");
        Environment.SetEnvironmentVariable("EXA_API_KEY", null);
        Environment.SetEnvironmentVariable("exaApiKey", null);
        try
        {
            RecordingHandler handler = new("Title: x\nURL: https://example.test/");
            HttpClient httpClient = new(handler);
            WebSearchService sut = new(httpClient);

            await sut.RunAsync(
                new WebSearchRequest("short", [new WebSearchQuery("dotnet")]),
                "session_1",
                CancellationToken.None);

            handler.RequestUris.Should().NotBeEmpty();
            handler.RequestUris.Should().OnlyContain(uri => string.IsNullOrEmpty(uri.Query));
        }
        finally
        {
            Environment.SetEnvironmentVariable("EXA_API_KEY", originalUpper);
            Environment.SetEnvironmentVariable("exaApiKey", originalCamel);
        }
    }

    [Fact]
    public async Task RunAsync_Should_RecordWarning_When_SearchToolReturnsError()
    {
        RecordingHandler handler = new("rate limit exceeded", isError: true);
        HttpClient httpClient = new(handler);
        WebSearchService sut = new(httpClient);

        WebSearchResult result = await sut.RunAsync(
            new WebSearchRequest("short", [new WebSearchQuery("dotnet")]),
            "session_1",
            CancellationToken.None);

        result.SearchQuery.Should().ContainSingle();
        result.SearchQuery[0].Results.Should().BeEmpty();
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("rate limit exceeded");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _searchText;
        private readonly bool _isError;

        public RecordingHandler(string searchText, bool isError = false)
        {
            _searchText = searchText;
            _isError = isError;
        }

        public List<string> Methods { get; } = [];

        public List<Uri> RequestUris { get; } = [];

        public string LastToolCall { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
            {
                RequestUris.Add(request.RequestUri);
            }

            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            using JsonDocument document = JsonDocument.Parse(body);
            string method = document.RootElement.GetProperty("method").GetString() ?? string.Empty;
            Methods.Add(method);

            if (method == "notifications/initialized")
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            if (method == "initialize")
            {
                HttpResponseMessage initializeResponse = JsonResponse(
                    """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","capabilities":{},"serverInfo":{"name":"exa","version":"1.0"}}}""");
                initializeResponse.Headers.TryAddWithoutValidation("Mcp-Session-Id", "test-session");
                return initializeResponse;
            }

            // tools/call
            LastToolCall = body;
            string escaped = JsonEncodedText.Encode(_searchText).ToString();
            string isErrorLiteral = _isError ? "true" : "false";
            string json = "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"isError\":"
                + isErrorLiteral
                + ",\"content\":[{\"type\":\"text\",\"text\":\""
                + escaped
                + "\"}]}}";
            return JsonResponse(json);
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
