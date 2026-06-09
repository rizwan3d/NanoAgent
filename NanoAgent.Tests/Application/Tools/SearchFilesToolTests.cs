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
    public void Schema_Should_ExposeSearchModesFiltersAndPaging()
    {
        SearchFilesTool sut = new(Mock.Of<IWorkspaceFileService>());

        using JsonDocument schema = JsonDocument.Parse(sut.Schema);
        JsonElement properties = schema.RootElement.GetProperty("properties");

        properties.TryGetProperty("mode", out _).Should().BeTrue();
        properties.TryGetProperty("regex", out _).Should().BeTrue();
        properties.TryGetProperty("wholeWord", out _).Should().BeTrue();
        properties.TryGetProperty("glob", out _).Should().BeTrue();
        properties.TryGetProperty("includeGlobs", out _).Should().BeTrue();
        properties.TryGetProperty("excludeGlobs", out _).Should().BeTrue();
        properties.TryGetProperty("fuzzy", out _).Should().BeTrue();
        properties.TryGetProperty("offset", out _).Should().BeTrue();
        properties.TryGetProperty("cursor", out _).Should().BeTrue();
        properties.TryGetProperty("includeHidden", out _).Should().BeTrue();
        properties.TryGetProperty("includeGenerated", out _).Should().BeTrue();
        properties.TryGetProperty("includeIgnored", out _).Should().BeTrue();
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
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_OffsetAndCursorAreCombined()
    {
        SearchFilesTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "query": "Program", "offset": 5, "cursor": "NQ==" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("does not allow 'offset' and 'cursor' together");
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
                    request.Mode == WorkspaceFileSearchModes.Fuzzy &&
                    !request.CaseSensitive &&
                    request.Glob == "**/*.cs" &&
                    request.Fuzzy &&
                    request.Limit == 5 &&
                    request.Offset == 0 &&
                    request.IncludeGlobs!.SequenceEqual(new[] { "**/*.cs" }) &&
                    request.ExcludeGlobs!.SequenceEqual(new[] { "**/obj/**" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceFileSearchResult(
                "Program",
                "src",
                [new WorkspaceFileSearchMatch("src/Program.cs", 9000, "filename_contains")],
                "**/*.cs",
                Fuzzy: true,
                Limit: 5,
                Mode: WorkspaceFileSearchModes.Fuzzy,
                TotalMatchCount: 1,
                IncludeGlobs: ["**/*.cs"],
                ExcludeGlobs: ["**/obj/**"]));

        SearchFilesTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "query": "Program", "path": "src", "caseSensitive": false, "glob": "**/*.cs", "excludeGlobs": ["**/obj/**"], "fuzzy": true, "limit": 5 }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("Program.cs");
        result.JsonResult.Should().Contain("filename_contains");
        result.RenderPayload!.Text.Should().Contain("src/Program.cs");
        result.RenderPayload.Text.Should().Contain("score=9000");
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
