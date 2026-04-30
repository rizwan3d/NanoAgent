using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class CodeIntelligenceToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_ActionIsMissing()
    {
        CodeIntelligenceTool sut = new(Mock.Of<ICodeIntelligenceService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "path": "Program.cs" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires action");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_PositionActionHasNoPosition()
    {
        CodeIntelligenceTool sut = new(Mock.Of<ICodeIntelligenceService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "action": "definition", "path": "Program.cs" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("line");
        result.Message.Should().Contain("character");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_RenameHasNoNewName()
    {
        CodeIntelligenceTool sut = new(Mock.Of<ICodeIntelligenceService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "action": "rename_symbol", "path": "Program.cs", "line": 5, "character": 3 }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("newName");
    }

    [Fact]
    public async Task ExecuteAsync_Should_QueryServiceWithResolvedPath()
    {
        string workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-CodeIntel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));

        try
        {
            ReplSessionContext session = TestSessionFactory.Create(workspaceRoot);
            session.TrySetWorkingDirectory("src", out string? error).Should().BeTrue(error);

            Mock<ICodeIntelligenceService> service = new(MockBehavior.Strict);
            service
                .Setup(sut => sut.QueryAsync(
                    It.Is<CodeIntelligenceRequest>(request =>
                        request.Action == "definition" &&
                        request.Path == "src/Program.cs" &&
                        request.Line == 5 &&
                        request.Character == 3 &&
                        request.TimeoutSeconds == 4 &&
                        !request.IncludeDeclaration),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CodeIntelligenceResult(
                    "definition",
                    "src/Program.cs",
                    "csharp",
                    "C# language server",
                    [new CodeIntelligenceItem("Definition", null, null, "src/Program.cs", 10, 1, 10, 15, null)],
                    HoverText: null,
                    Warnings: []));

            CodeIntelligenceTool sut = new(service.Object);

            ToolResult result = await sut.ExecuteAsync(
                CreateContext(
                    """{ "action": "definition", "path": "Program.cs", "line": 5, "character": 3, "timeoutSeconds": 4 }""",
                    session),
                CancellationToken.None);

            result.Status.Should().Be(ToolResultStatus.Success);
            result.JsonResult.Should().Contain("src/Program.cs");
            result.RenderPayload!.Text.Should().Contain("Definition");
            service.VerifyAll();
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_QueryServiceWithExpandedOperationArguments()
    {
        Mock<ICodeIntelligenceService> service = new(MockBehavior.Strict);
        service
            .Setup(sut => sut.QueryAsync(
                It.Is<CodeIntelligenceRequest>(request =>
                    request.Action == "call_hierarchy" &&
                    request.Path == "Program.cs" &&
                    request.Line == 5 &&
                    request.Character == 3 &&
                    request.CallDirection == "incoming" &&
                    request.Query == "Example" &&
                    request.NewName == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeIntelligenceResult(
                "call_hierarchy",
                "Program.cs",
                "csharp",
                "C# language server",
                [new CodeIntelligenceItem("IncomingCall", "Caller", null, "Program.cs", 10, 1, 10, 15, null)],
                HoverText: null,
                Warnings: []));

        CodeIntelligenceTool sut = new(service.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "action": "call_hierarchy", "path": "Program.cs", "line": 5, "character": 3, "callDirection": "incoming", "query": "Example" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("IncomingCall");
        service.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnNotFound_When_ServerIsUnavailable()
    {
        Mock<ICodeIntelligenceService> service = new(MockBehavior.Strict);
        service
            .Setup(sut => sut.QueryAsync(
                It.IsAny<CodeIntelligenceRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CodeIntelligenceUnavailableException(
                "No language server could complete the request.",
                ["C# language server: command was not found."]));

        CodeIntelligenceTool sut = new(service.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "action": "document_symbols", "path": "Program.cs" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.NotFound);
        result.RenderPayload!.Text.Should().Contain("command was not found");
    }

    private static ToolExecutionContext CreateContext(
        string argumentsJson,
        ReplSessionContext? session = null)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "code_intelligence",
            document.RootElement.Clone(),
            session ?? TestSessionFactory.Create());
    }
}
