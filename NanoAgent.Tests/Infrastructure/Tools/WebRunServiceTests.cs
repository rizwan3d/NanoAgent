using FluentAssertions;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.Tools;
using System.Net;
using System.Text;

namespace NanoAgent.Tests.Infrastructure.Tools;

public sealed class WebRunServiceTests
{
    [Fact]
    public async Task RunAsync_Should_RequestDuckDuckGoHtmlEndpoint_AndParseSearchResults()
    {
        RecordingHandler handler = new();
        handler.EnqueueHtml(
            """
            <!DOCTYPE html>
            <html>
            <body>
              <div class="result results_links results_links_deep web-result ">
                <div class="links_main links_deep result__body">
                  <h2 class="result__title">
                    <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Flearn.microsoft.com%2Fen-us%2Fdotnet%2F">.NET documentation</a>
                  </h2>
                  <div class="result__extras">
                    <div class="result__extras__url">
                      <a class="result__url" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Flearn.microsoft.com%2Fen-us%2Fdotnet%2F">
                        learn.microsoft.com/en-us/dotnet/
                      </a>
                    </div>
                  </div>
                  <a class="result__snippet" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Flearn.microsoft.com%2Fen-us%2Fdotnet%2F">Learn to use <b>.NET</b> on any platform.</a>
                  <div class="clear"></div>
                </div>
              </div>
              <div class="result results_links results_links_deep web-result ">
                <div class="links_main links_deep result__body">
                  <h2 class="result__title">
                    <a rel="nofollow" class="result__a" href="https://dotnet.microsoft.com/">.NET home</a>
                  </h2>
                  <a class="result__snippet" href="https://dotnet.microsoft.com/">Official site.</a>
                  <div class="clear"></div>
                </div>
              </div>
              <div class="nav-link"></div>
            </body>
            </html>
            """);

        HttpClient httpClient = new(handler);
        WebRunService sut = new(httpClient);

        WebRunResult result = await sut.RunAsync(
            new WebRunRequest(
                "medium",
                [new WebRunSearchQuery("dotnet")],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []),
            "session_1",
            CancellationToken.None);

        handler.RequestUris.Should().ContainSingle()
            .Which.Should().Be(new Uri("https://html.duckduckgo.com/html/?q=dotnet"));
        result.SearchQuery.Should().ContainSingle();
        result.SearchQuery[0].Query.Should().Be("dotnet");
        result.SearchQuery[0].Results.Should().HaveCount(2);
        result.SearchQuery[0].Results[0].Title.Should().Be(".NET documentation");
        result.SearchQuery[0].Results[0].Url.Should().Be("https://learn.microsoft.com/en-us/dotnet/");
        result.SearchQuery[0].Results[0].DisplayUrl.Should().Be("learn.microsoft.com/en-us/dotnet/");
        result.SearchQuery[0].Results[0].Snippet.Should().Be("Learn to use .NET on any platform.");
        result.SearchQuery[0].Results[1].Url.Should().Be("https://dotnet.microsoft.com/");
        result.SearchQuery[0].Results[0].RefId.Should().StartWith("web_run_");
    }

    [Fact]
    public async Task RunAsync_Should_OpenAndFindText_FromDirectUrl()
    {
        RecordingHandler handler = new();
        handler.EnqueueHtml(
            """
            <html>
            <body>
              <h1>Example page</h1>
              <p>Alpha line</p>
              <p>Beta keyword line</p>
              <p>Gamma line</p>
            </body>
            </html>
            """);
        handler.EnqueueHtml(
            """
            <html>
            <body>
              <h1>Example page</h1>
              <p>Alpha line</p>
              <p>Beta keyword line</p>
              <p>Gamma line</p>
            </body>
            </html>
            """);

        HttpClient httpClient = new(handler);
        WebRunService sut = new(httpClient);

        WebRunResult result = await sut.RunAsync(
            new WebRunRequest(
                "medium",
                [],
                [],
                [new WebRunOpenRequest("https://example.com/article", 2)],
                [new WebRunFindRequest("https://example.com/article", "keyword")],
                [],
                [],
                [],
                [],
                []),
            "session_1",
            CancellationToken.None);

        handler.RequestUris.Should().HaveCount(2);
        result.Open.Should().ContainSingle();
        result.Open[0].ResolvedUrl.Should().Be("https://example.com/article");
        result.Open[0].Text.Should().Contain("2: Alpha line");
        result.Find.Should().ContainSingle();
        result.Find[0].Matches.Should().ContainSingle();
        result.Find[0].Matches[0].LineText.Should().Be("Beta keyword line");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<Uri> RequestUris { get; } = [];

        public void EnqueueHtml(string responseBody)
        {
            _responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "text/html")
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.RequestUri.Should().NotBeNull();
            RequestUris.Add(request.RequestUri!);
            _responses.Should().NotBeEmpty("each test should enqueue a response for every request");
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
