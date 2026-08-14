using FluentAssertions;
using Moq;
using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Application.Tools;
using StemCode.Application.Tools.Models;
using System.Text.Json;

namespace StemCode.Tests.Application.Tools;

public sealed class SearchAndReplaceToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_SearchIsMissing()
    {
        SearchAndReplaceTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md", "replace": "new" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("'search' string");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnNotFound_When_NoMatchesExist()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.SearchAndReplaceWithTrackingAsync(
                "README.md",
                "old",
                "new",
                false,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceSearchAndReplaceExecutionResult(
                new WorkspaceSearchAndReplaceResult(
                    "README.md",
                    "old",
                    "new",
                    false,
                    true,
                    0,
                    12,
                    0,
                    0,
                    [],
                    0),
                null));

        SearchAndReplaceTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md", "search": "old", "replace": "new" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.NotFound);
        result.Message.Should().Contain("No matches found");
        workspaceFileService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ApplyTrackedReplacement_When_ArgumentsAreValid()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.SearchAndReplaceWithTrackingAsync(
                "README.md",
                "oldName",
                "newName",
                false,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceSearchAndReplaceExecutionResult(
                new WorkspaceSearchAndReplaceResult(
                    "README.md",
                    "oldName",
                    "newName",
                    false,
                    true,
                    2,
                    14,
                    1,
                    1,
                    [
                        new WorkspaceFileWritePreviewLine(1, "remove", "oldName = 1;"),
                        new WorkspaceFileWritePreviewLine(1, "add", "newName = 1;")
                    ],
                    0),
                new WorkspaceFileEditTransaction(
                    "search_and_replace (README.md)",
                    [new WorkspaceFileEditState("README.md", exists: true, content: "oldName = 1;")],
                    [new WorkspaceFileEditState("README.md", exists: true, content: "newName = 1;")])));

        SearchAndReplaceTool sut = new(workspaceFileService.Object);
        ReplSessionContext session = TestSessionFactory.Create();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md", "search": "oldName", "replace": "newName" }""", session),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("\"ReplacementCount\":2");
        result.RenderPayload!.Title.Should().Contain("README.md");
        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction).Should().BeTrue();
        transaction!.Description.Should().Be("search_and_replace (README.md)");
    }

    private static ToolExecutionContext CreateContext(
        string argumentsJson,
        ReplSessionContext? session = null)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "search_and_replace",
            document.RootElement.Clone(),
            session ?? TestSessionFactory.Create());
    }
}
