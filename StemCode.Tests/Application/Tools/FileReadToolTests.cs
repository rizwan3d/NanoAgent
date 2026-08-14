using FluentAssertions;
using Moq;
using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Application.Tools;
using StemCode.Application.Tools.Models;
using System.Text.Json;

namespace StemCode.Tests.Application.Tools;

[Collection(global::StemCode.Tests.TestCollections.SecretRedactorState)]
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
            .Setup(service => service.ReadFileAsync("README.md", 1, 2_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateReadResult("README.md", "1: hello", 1, 1, 1));

        FileReadTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.Message.Should().Contain("README.md");
        result.JsonResult.Should().Contain("\"Path\":\"README.md\"");
        result.RenderPayload.Should().NotBeNull();
        result.RenderPayload!.Text.Should().Be(
            "<path>README.md</path>\n<content>\n1: hello\n</content>\n\nShowing lines 1-1 of 1.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_NotSerializeDuplicateContentProperties()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.ReadFileAsync("README.md", 1, 2_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceFileReadResult(
                "README.md",
                rawContent: "1: hello",
                displayContent: "1: hello",
                startLine: 1,
                endLine: 1,
                totalLines: 1,
                truncated: false,
                nextOffset: null,
                sha256: "abc123",
                encoding: "utf-8"));

        FileReadTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md" }"""),
            CancellationToken.None);

        using JsonDocument jsonResult = JsonDocument.Parse(result.JsonResult);
        jsonResult.RootElement.TryGetProperty("Content", out _).Should().BeFalse();
        jsonResult.RootElement.GetProperty("RawContent").GetString().Should().Be("1: hello");
        jsonResult.RootElement.GetProperty("DisplayContent").GetString().Should().Be("1: hello");
    }

    [Fact]
    public async Task ExecuteAsync_Should_RedactEnvironmentFileContents()
    {
        bool originalValue = StemCode.Application.Utilities.SecretRedactor.IsEnabled;
        StemCode.Application.Utilities.SecretRedactor.IsEnabled = true;

        try
        {
            Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
            workspaceFileService
                .Setup(service => service.ReadFileAsync(".env", 1, 2_000, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateReadResult(
                    ".env",
                    "1: NODE_ENV=development\n2: DATABASE_URL=postgres://user:pass@example/db",
                    1,
                    2,
                    2));

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
        finally
        {
            StemCode.Application.Utilities.SecretRedactor.IsEnabled = originalValue;
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_AcceptExplicitOffsetAndLimit()
    {
        Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
        workspaceFileService
            .Setup(service => service.ReadFileAsync("notes.md", 25, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateReadResult("notes.md", "25: alpha", 25, 25, 50));

        FileReadTool sut = new(workspaceFileService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "notes.md", "offset": 25, "limit": 100 }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        using JsonDocument jsonResult = JsonDocument.Parse(result.JsonResult);
        jsonResult.RootElement.GetProperty("StartLine").GetInt32().Should().Be(25);
        jsonResult.RootElement.GetProperty("EndLine").GetInt32().Should().Be(25);
        jsonResult.RootElement.GetProperty("TotalLines").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_OffsetIsNotPositive()
    {
        FileReadTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md", "offset": 0 }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("'offset' to be a positive integer");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_LimitIsNotPositive()
    {
        FileReadTool sut = new(Mock.Of<IWorkspaceFileService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "README.md", "limit": 0 }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("'limit' to be a positive integer");
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

    private static WorkspaceFileReadResult CreateReadResult(
        string path,
        string content,
        int startLine,
        int endLine,
        int totalLines,
        bool truncated = false,
        int? nextOffset = null)
    {
        return new WorkspaceFileReadResult(
            path,
            content,
            startLine,
            endLine,
            totalLines,
            truncated,
            nextOffset,
            "abc123",
            "utf-8");
    }
}
