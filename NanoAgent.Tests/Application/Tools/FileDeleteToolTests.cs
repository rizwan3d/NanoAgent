using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class FileDeleteToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_PathIsMissing()
    {
        FileDeleteTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires a non-empty 'path'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_DeleteFile_When_ArgumentsAreValid()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.DeleteFileWithTrackingAsync(
                "README.md",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceFileDeleteExecutionResult(
                new WorkspaceFileDeleteResult(
                    "README.md",
                    5,
                    0,
                    1,
                    [new WorkspaceFileWritePreviewLine(1, "remove", "hello")],
                    0),
                new WorkspaceFileEditTransaction(
                    "file_delete (README.md)",
                    [new WorkspaceFileEditState("README.md", exists: true, content: "hello")],
                    [new WorkspaceFileEditState("README.md", exists: false, content: null)])));

        FileDeleteTool sut = new(workspaceFileService.Object);
        ReplSessionContext session = TestSessionFactory.Create();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md" }""", session),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("\"Path\":\"README.md\"");
        result.JsonResult.Should().Contain("\"RemovedLineCount\":1");
        result.RenderPayload!.Title.Should().Contain("README.md");
        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction).Should().BeTrue();
        transaction!.Description.Should().Be("file_delete (README.md)");
        workspaceFileService.VerifyAll();
    }

    private static ToolExecutionContext CreateContext(
        string argumentsJson,
        ReplSessionContext? session = null)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "file_delete",
            document.RootElement.Clone(),
            session ?? TestSessionFactory.Create());
    }
}
