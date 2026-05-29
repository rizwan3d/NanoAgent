using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Tests.Application.Tools;

namespace NanoAgent.Tests.Application.Commands;

public sealed class LspCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ListDetectedLanguageServers()
    {
        Mock<ICodeIntelligenceService> service = new(MockBehavior.Strict);
        service
            .Setup(sut => sut.QueryAsync(
                It.Is<CodeIntelligenceRequest>(request =>
                    request.Action == "servers_status" &&
                    request.Refresh == false),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStatusResult());

        LspCommandHandler sut = new(service.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("lsp"),
            CancellationToken.None);

        result.Message.Should().Contain("Language servers:");
        result.Message.Should().Contain("Python");
        result.Message.Should().Contain("Pyright");
        service.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_RefreshStatus_When_Requested()
    {
        Mock<ICodeIntelligenceService> service = new(MockBehavior.Strict);
        service
            .Setup(sut => sut.QueryAsync(
                It.Is<CodeIntelligenceRequest>(request =>
                    request.Action == "servers_status" &&
                    request.Refresh),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStatusResult());

        LspCommandHandler sut = new(service.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("lsp", "refresh", ["refresh"]),
            CancellationToken.None);

        result.Message.Should().Contain("Language servers:");
        service.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_FilterCandidates_For_File()
    {
        ReplSessionContext session = TestSessionFactory.Create();
        Mock<ICodeIntelligenceService> service = new(MockBehavior.Strict);
        service
            .Setup(sut => sut.QueryAsync(
                It.IsAny<CodeIntelligenceRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStatusResult());

        LspCommandHandler sut = new(service.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("lsp", "file src/app.py", ["file", "src/app.py"], session),
            CancellationToken.None);

        result.Message.Should().Contain("LSP candidates for src/app.py (.py):");
        result.Message.Should().Contain("Selected: Pyright");
        result.Message.Should().NotContain("TypeScript");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnUsage_For_InvalidArguments()
    {
        LspCommandHandler sut = new(Mock.Of<ICodeIntelligenceService>());

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("lsp", "file", ["file"]),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Be("Usage: /lsp file <path> [refresh]");
    }

    private static CodeIntelligenceResult CreateStatusResult()
    {
        return new CodeIntelligenceResult(
            "servers_status",
            ".",
            "multi",
            "registry",
            [],
            HoverText: null,
            Warnings: [],
            Servers:
            [
                new CodeIntelligenceServerStatus(
                    "Python",
                    "python",
                    [".py"],
                    [
                        new CodeIntelligenceServerCandidate(
                            "python-pyright",
                            "Pyright",
                            "pyright-langserver",
                            ["--stdio"],
                            200,
                            "detected",
                            "built-in",
                            "C:\\tools\\pyright-langserver.cmd",
                            "npm install -g pyright",
                            "python",
                            null)
                    ],
                    "Pyright"),
                new CodeIntelligenceServerStatus(
                    "TypeScript/JavaScript",
                    "typescript",
                    [".ts", ".js"],
                    [
                        new CodeIntelligenceServerCandidate(
                            "ts-vtsls",
                            "VTSLS",
                            "vtsls",
                            ["--stdio"],
                            200,
                            "missing",
                            "built-in",
                            null,
                            "npm install -g @vtsls/language-server typescript",
                            "typescript",
                            "Command 'vtsls' was not found.")
                    ],
                    null)
            ]);
    }

    private static ReplCommandContext CreateContext(
        string commandName,
        string argumentText = "",
        IReadOnlyList<string>? arguments = null,
        ReplSessionContext? session = null)
    {
        return new ReplCommandContext(
            commandName,
            argumentText,
            arguments ?? [],
            string.IsNullOrWhiteSpace(argumentText)
                ? $"/{commandName}"
                : $"/{commandName} {argumentText}",
            session ?? TestSessionFactory.Create());
    }
}
