using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class TextSearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_QueryIsMissing()
    {
        TextSearchTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires a non-empty 'query'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnSearchMatches_When_QueryIsValid()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.SearchTextAsync(
                It.Is<WorkspaceTextSearchRequest>(request =>
                    request.Query == "TODO" &&
                    request.Path == "src" &&
                    request.CaseSensitive),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceTextSearchResult(
                "TODO",
                "src",
                [new WorkspaceTextSearchMatch("src/Program.cs", 12, "// TODO: refactor")]));

        TextSearchTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "query": "TODO", "path": "src", "caseSensitive": true }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("TODO");
        result.RenderPayload!.Text.Should().Contain("src/Program.cs:12");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "text_search",
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }
}
