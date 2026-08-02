using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class InsertContentToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_LineIsMissing()
    {
        InsertContentTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "Program.cs", "content": "using System;\n" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("positive integer 'line'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_InsertContent_When_ArgumentsAreValid()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.InsertContentWithTrackingAsync(
                "Program.cs",
                1,
                "using System;\n",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceFileInsertExecutionResult(
                new WorkspaceFileInsertResult(
                    "Program.cs",
                    1,
                    1,
                    14,
                    1,
                    0,
                    [new WorkspaceFileWritePreviewLine(1, "add", "using System;")],
                    0),
                new WorkspaceFileEditTransaction(
                    "insert_content (Program.cs)",
                    [new WorkspaceFileEditState("Program.cs", exists: true, content: "class Program {}\n")],
                    [new WorkspaceFileEditState("Program.cs", exists: true, content: "using System;\nclass Program {}\n")])));

        InsertContentTool sut = new(workspaceFileService.Object);
        ReplSessionContext session = TestSessionFactory.Create();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "Program.cs", "line": 1, "content": "using System;\n" }""", session),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("\"Line\":1");
        result.JsonResult.Should().Contain("\"InsertedLineCount\":1");
        result.RenderPayload!.Title.Should().Contain("Program.cs");
        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction).Should().BeTrue();
        transaction!.Description.Should().Be("insert_content (Program.cs)");
        workspaceFileService.VerifyAll();
    }

    private static ToolExecutionContext CreateContext(
        string argumentsJson,
        ReplSessionContext? session = null)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "insert_content",
            document.RootElement.Clone(),
            session ?? TestSessionFactory.Create());
    }
}
