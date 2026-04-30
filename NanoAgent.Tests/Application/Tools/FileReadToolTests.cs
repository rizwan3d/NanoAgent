using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class FileReadToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_PathIsMissing()
    {
        FileReadTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires a non-empty 'path'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnStructuredResult_When_FileIsRead()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.ReadFileAsync("README.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceFileReadResult("README.md", "hello", 5));

        FileReadTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.Message.Should().Contain("README.md");
        result.JsonResult.Should().Contain("\"Path\":\"README.md\"");
        result.RenderPayload.Should().NotBeNull();
        result.RenderPayload!.Text.Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_Should_RedactEnvironmentFileContents()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.ReadFileAsync(".env", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceFileReadResult(
                ".env",
                "NODE_ENV=development\nDATABASE_URL=postgres://user:pass@example/db",
                63));

        FileReadTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": ".env" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("NODE_ENV");
        result.JsonResult.Should().Contain("DATABASE_URL");
        result.JsonResult.Should().Contain("redacted");
        result.JsonResult.Should().NotContain("postgres://");
        result.RenderPayload!.Text.Should().NotContain("development");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "file_read",
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }
}
