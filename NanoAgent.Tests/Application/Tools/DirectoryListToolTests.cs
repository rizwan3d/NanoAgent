using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class DirectoryListToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ListDirectoryContents()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.ListDirectoryAsync(
                "src",
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceDirectoryListResult(
                "src",
                [new WorkspaceDirectoryEntry("src/Program.cs", "file")]));

        DirectoryListTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "src", "recursive": true }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("Program.cs");
        result.RenderPayload!.Text.Should().Contain("file: src/Program.cs");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "directory_list",
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }
}
