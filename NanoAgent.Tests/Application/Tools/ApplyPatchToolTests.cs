using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class ApplyPatchToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_PatchIsMissing()
    {
        ApplyPatchTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires a non-empty 'patch'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnStructuredResult_When_PatchApplies()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.ApplyPatchWithTrackingAsync(
                "*** Begin Patch\n*** End Patch",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceApplyPatchExecutionResult(
                new WorkspaceApplyPatchResult(
                    1,
                    1,
                    0,
                    [
                        new WorkspaceApplyPatchFileResult(
                            "README.md",
                            "update",
                            null,
                            1,
                            0,
                            [new WorkspaceFileWritePreviewLine(1, "add", "hello")],
                            0)
                    ]),
                new WorkspaceFileEditTransaction(
                    "apply_patch (1 file)",
                    [new WorkspaceFileEditState("README.md", exists: true, content: "old")],
                    [new WorkspaceFileEditState("README.md", exists: true, content: "hello")])));

        ApplyPatchTool sut = new(workspaceFileService.Object);
        ReplSessionContext session = TestSessionFactory.Create();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "patch": "*** Begin Patch\n*** End Patch" }""", session),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("\"FileCount\":1");
        result.RenderPayload!.Text.Should().Contain("README.md");
        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction).Should().BeTrue();
        transaction!.Description.Should().Be("apply_patch (1 file)");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnRetryGuidance_When_PatchFormatIsInvalid()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.ApplyPatchWithTrackingAsync(
                "*** Begin Patch\n*** Update File: README.md",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("Patch text must end with '*** End Patch'."));

        ApplyPatchTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "patch": "*** Begin Patch\n*** Update File: README.md" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("Patch text must end with '*** End Patch'.");
        result.Message.Should().Contain("Call apply_patch again with corrected patch text.");
        result.Message.Should().Contain("final non-empty line must be exactly '*** End Patch'");
        result.JsonResult.Should().Contain("Call apply_patch again with corrected patch text.");
        result.RenderPayload.Should().NotBeNull();
        result.RenderPayload!.Title.Should().Be("Patch rejected");
        result.RenderPayload.Text.Should().Contain("first non-empty line must be exactly '*** Begin Patch'");
        result.RenderPayload.Text.Should().Contain("final non-empty line must be exactly '*** End Patch'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ResolvePatchPathsFromSessionWorkingDirectory()
    {
        string workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-PatchCwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "ToDoApp"));

        try
        {
            ReplSessionContext session = TestSessionFactory.Create(workspaceRoot);
            session.TrySetWorkingDirectory("ToDoApp", out string? error).Should().BeTrue(error);

            Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
            workspaceFileService
                .Setup(service => service.ApplyPatchWithTrackingAsync(
                    "*** Begin Patch\n*** Update File: ToDoApp/Program.cs\n@@\n-old\n+new\n*** End Patch",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkspaceApplyPatchExecutionResult(
                    new WorkspaceApplyPatchResult(
                        1,
                        1,
                        1,
                        [
                            new WorkspaceApplyPatchFileResult(
                                "ToDoApp/Program.cs",
                                "update",
                                null,
                                1,
                                1,
                                [],
                                0)
                        ]),
                    new WorkspaceFileEditTransaction(
                        "apply_patch (1 file)",
                        [new WorkspaceFileEditState("ToDoApp/Program.cs", exists: true, content: "old")],
                        [new WorkspaceFileEditState("ToDoApp/Program.cs", exists: true, content: "new")])));

            ApplyPatchTool sut = new(workspaceFileService.Object);

            ToolResult result = await sut.ExecuteAsync(
                CreateContext("""{ "patch": "*** Begin Patch\n*** Update File: Program.cs\n@@\n-old\n+new\n*** End Patch" }""", session),
                CancellationToken.None);

            result.Status.Should().Be(ToolResultStatus.Success);
            workspaceFileService.VerifyAll();
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    private static ToolExecutionContext CreateContext(
        string argumentsJson,
        ReplSessionContext? session = null)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "apply_patch",
            document.RootElement.Clone(),
            session ?? TestSessionFactory.Create());
    }
}
