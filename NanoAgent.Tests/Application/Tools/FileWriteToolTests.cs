using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class FileWriteToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_ContentIsMissing()
    {
        FileWriteTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("'content' string");
    }

    [Fact]
    public async Task ExecuteAsync_Should_WriteFile_When_ArgumentsAreValid()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.WriteFileWithTrackingAsync(
                "README.md",
                "hello",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceFileWriteExecutionResult(
                new WorkspaceFileWriteResult(
                    "README.md",
                    false,
                    5,
                    1,
                    0,
                    [new WorkspaceFileWritePreviewLine(1, "add", "hello")],
                    0),
                new WorkspaceFileEditTransaction(
                    "file_write (README.md)",
                    [new WorkspaceFileEditState("README.md", exists: false, content: null)],
                    [new WorkspaceFileEditState("README.md", exists: true, content: "hello")])));

        FileWriteTool sut = new(workspaceFileService.Object);
        ReplSessionContext session = TestSessionFactory.Create();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md", "content": "hello", "overwrite": false }""", session),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("\"OverwroteExistingFile\":false");
        result.RenderPayload!.Title.Should().Contain("README.md");
        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction).Should().BeTrue();
        transaction!.Description.Should().Be("file_write (README.md)");
    }

    [Fact]
    public async Task ExecuteAsync_Should_PassEmptyContentToWorkspaceService()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.WriteFileWithTrackingAsync(
                ".gitkeep",
                string.Empty,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceFileWriteExecutionResult(
                new WorkspaceFileWriteResult(
                    ".gitkeep",
                    false,
                    0,
                    0,
                    0,
                    [],
                    0),
                new WorkspaceFileEditTransaction(
                    "file_write (.gitkeep)",
                    [new WorkspaceFileEditState(".gitkeep", exists: false, content: null)],
                    [new WorkspaceFileEditState(".gitkeep", exists: true, content: string.Empty)])));

        FileWriteTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": ".gitkeep", "content": "" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        workspaceFileService.VerifyAll();
    }

    private static ToolExecutionContext CreateContext(
        string argumentsJson,
        ReplSessionContext? session = null)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "file_write",
            document.RootElement.Clone(),
            session ?? TestSessionFactory.Create());
    }
}
