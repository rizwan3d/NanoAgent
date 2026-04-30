using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class WebRunToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_RequestHasNoOperations()
    {
        WebRunTool sut = new(Mock.Of<IWebRunService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("Provide at least one operation array");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_ResponseLengthIsInvalid()
    {
        WebRunTool sut = new(Mock.Of<IWebRunService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "response_length": "verbose", "search_query": [{ "q": "dotnet" }] }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("short, medium, or long");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnStructuredResults_When_RequestIsValid()
    {
        Mock<IWebRunService> webRunService = new(MockBehavior.Strict);
        webRunService
            .Setup(service => service.RunAsync(
                It.Is<WebRunRequest>(request =>
                    request.ResponseLength == "short" &&
                    request.SearchQuery.Count == 1 &&
                    request.SearchQuery[0].Query == "dotnet" &&
                    request.ImageQuery.Count == 0 &&
                    request.Open.Count == 0),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebRunResult(
                "short",
                [
                    new WebRunSearchResult(
                        "dotnet",
                        [
                            new WebRunSearchItem(
                                "web_run_1",
                                ".NET documentation",
                                "https://learn.microsoft.com/en-us/dotnet/",
                                "learn.microsoft.com/en-us/dotnet/",
                                "Learn to use .NET.")
                        ])
                ],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []));

        WebRunTool sut = new(webRunService.Object);

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
            "web_run",
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }
}
