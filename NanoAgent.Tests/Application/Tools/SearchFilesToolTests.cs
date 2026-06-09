using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class SearchFilesToolTests
{
    [Fact]
    public void Schema_Should_ExposeGlobFuzzyAndLimit()
    {
        SearchFilesTool sut = new(Mock.Of<IWorkspaceFileService>());

        using JsonDocument schema = JsonDocument.Parse(sut.Schema);
        JsonElement properties = schema.RootElement.GetProperty("properties");

        properties.TryGetProperty("glob", out _).Should().BeTrue();
        properties.TryGetProperty("fuzzy", out _).Should().BeTrue();
        properties.TryGetProperty("limit", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_QueryIsMissing()
    {
        SearchFilesTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires a non-empty 'query'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_LimitIsOutOfRange()
    {
        SearchFilesTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "query": "Program", "limit": 0 }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("'limit' to be between 1 and 200");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnStructuredMatches_When_QueryIsValid()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.SearchFilesAsync(
                It.Is<WorkspaceFileSearchRequest>(request =>
                    request.Query == "Program" &&
                    request.Path == "src" &&
                    !request.CaseSensitive &&
                    request.Glob == "**/*.cs" &&
                    request.Fuzzy &&
                    request.Limit == 5),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceFileSearchResult(
                "Program",
                "src",
                ["src/Program.cs"],
                "**/*.cs",
                Fuzzy: true,
                Limit: 5));

        SearchFilesTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "query": "Program", "path": "src", "caseSensitive": false, "glob": "**/*.cs", "fuzzy": true, "limit": 5 }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("Program.cs");
        result.RenderPayload!.Text.Should().Contain("src/Program.cs");
        result.RenderPayload.Text.Should().Contain("glob=**/*.cs");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "search_files",
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }
}
