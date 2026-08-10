using FluentAssertions;
using Moq;
using StemCode.Application.Abstractions;
using StemCode.Application.Commands;
using StemCode.Application.Models;
using StemCode.Application.Tools.Models;
using StemCode.Tests.Application.Tools;

namespace StemCode.Tests.Application.Commands;

public sealed class TerminalsCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ListSessionBackgroundTerminals()
    {
        ReplSessionContext session = TestSessionFactory.Create();
        Mock<IShellCommandService> shellCommandService = new(MockBehavior.Strict);
        shellCommandService
            .Setup(service => service.ListBackgroundAsync(
                session.SessionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BackgroundTerminalInfo(
                    "terminal-1",
                    session.SessionId,
                    "npm run dev",
                    ".",
                    "running",
                    null,
                    DateTimeOffset.Parse("2026-05-09T12:00:00Z"),
                    null,
                    null)
            ]);
        TerminalsCommandHandler sut = new(shellCommandService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(session),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("terminal-1");
        result.Message.Should().Contain("npm run dev");
        shellCommandService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_StopTerminal_When_ItBelongsToSession()
    {
        ReplSessionContext session = TestSessionFactory.Create();
        Mock<IShellCommandService> shellCommandService = new(MockBehavior.Strict);
        shellCommandService
            .Setup(service => service.ListBackgroundAsync(
                session.SessionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BackgroundTerminalInfo(
                    "terminal-1",
                    session.SessionId,
                    "npm run dev",
                    ".",
                    "running",
                    null,
                    DateTimeOffset.Parse("2026-05-09T12:00:00Z"),
                    null,
                    null)
            ]);
        shellCommandService
            .Setup(service => service.StopBackgroundAsync(
                "terminal-1",
                session.SessionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShellCommandExecutionResult(
                "npm run dev",
                ".",
                0,
                string.Empty,
                string.Empty,
                Background: true,
                TerminalId: "terminal-1",
                TerminalStatus: "stopped",
                TerminalAction: "stop"));
        TerminalsCommandHandler sut = new(shellCommandService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(session, "stop terminal-1", ["stop", "terminal-1"]),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Be("Stopped background terminal 'terminal-1'.");
        shellCommandService.VerifyAll();
    }

    private static ReplCommandContext CreateContext(
        ReplSessionContext session,
        string argumentText = "",
        IReadOnlyList<string>? arguments = null)
    {
        return new ReplCommandContext(
            "terminals",
            argumentText,
            arguments ?? [],
            string.IsNullOrWhiteSpace(argumentText)
                ? "/terminals"
                : "/terminals " + argumentText,
            session);
    }
}
