using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class WebSearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_RequestHasNoSearches()
    {
        WebSearchTool sut = new(Mock.Of<IWebSearchService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("Provide at least one search");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_ResponseLengthIsInvalid()
    {
        WebSearchTool sut = new(Mock.Of<IWebSearchService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "response_length": "verbose", "search_query": [{ "q": "dotnet" }] }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("short, medium, or long");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnStructuredResults_When_RequestIsValid()
    {
        Mock<IWebSearchService> webSearchService = new(MockBehavior.Strict);
        webSearchService
            .Setup(service => service.RunAsync(
                It.Is<WebSearchRequest>(request =>
                    request.ResponseLength == "short" &&
                    request.SearchQuery.Count == 1 &&
                    request.SearchQuery[0].Query == "dotnet"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebSearchResult(
                "short",
                [
                    new WebSearchQueryResult(
                        "dotnet",
                        "Title: .NET documentation\nURL: https://learn.microsoft.com/en-us/dotnet/",
                        [
                            new WebSearchItem(
                                ".NET documentation",
                                "https://learn.microsoft.com/en-us/dotnet/")
                        ])
                ],
                []));

        WebSearchTool sut = new(webSearchService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "response_length": "short", "search_query": [{ "q": "dotnet" }] }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("learn.microsoft.com");
        result.RenderPayload!.Text.Should().Contain("Search 'dotnet': 1 result(s)");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "web_search",
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }
}
