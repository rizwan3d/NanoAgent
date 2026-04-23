using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Presentation.Abstractions;
using NanoAgent.Presentation.Repl.Commands;
using NanoAgent.Presentation.Repl.Services;
using NanoAgent.Domain.Models;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Repl.Services;

public sealed class ReplCommandDispatcherTests
{
    private static readonly ReplSessionContext Session = new(
        new AgentProviderProfile(ProviderKind.OpenAi, null),
        "gpt-5-mini",
        ["gpt-5-mini", "gpt-4.1"]);

    [Fact]
    public async Task DispatchAsync_Should_ReturnExitRequested_When_ExitCommandIsHandled()
    {
        ReplCommandDispatcher sut = new([
            new ExitCommandHandler(),
            new HelpCommandHandler()
        ]);

        ReplCommandResult result = await sut.DispatchAsync(
            new ParsedReplCommand("/exit", "exit", string.Empty, []),
            Session,
            CancellationToken.None);

        result.ExitRequested.Should().BeTrue();
        result.Message.Should().Be("Exiting NanoAgent.");
    }

    [Fact]
    public async Task DispatchAsync_Should_ReturnUnknownCommandMessage_When_CommandDoesNotExist()
    {
        ReplCommandDispatcher sut = new([
            new ExitCommandHandler()
        ]);

        ReplCommandResult result = await sut.DispatchAsync(
            new ParsedReplCommand("/unknown", "unknown", string.Empty, []),
            Session,
            CancellationToken.None);

        result.ExitRequested.Should().BeFalse();
        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Be("Unknown command '/unknown'. Type /help to see the available commands.");
    }

    [Fact]
    public async Task DispatchAsync_Should_PassArgumentsToHandler_When_CommandContainsArguments()
    {
        CapturingCommandHandler handler = new();
        ReplCommandDispatcher sut = new([handler]);

        ReplCommandResult result = await sut.DispatchAsync(
            new ParsedReplCommand("/echo hello world", "echo", "hello world", ["hello", "world"]),
            Session,
            CancellationToken.None);

        result.Should().Be(ReplCommandResult.Continue("handled"));
        handler.LastContext.Should().NotBeNull();
        handler.LastContext!.CommandName.Should().Be("echo");
        handler.LastContext.ArgumentText.Should().Be("hello world");
        handler.LastContext.Arguments.Should().Equal("hello", "world");
        handler.LastContext.RawText.Should().Be("/echo hello world");
    }

    private sealed class CapturingCommandHandler : IReplCommandHandler
    {
        public string CommandName => "echo";

        public string Description => "Capture arguments.";

        public string Usage => "/echo <text>";

        public ReplCommandContext? LastContext { get; private set; }

        public Task<ReplCommandResult> ExecuteAsync(
            ReplCommandContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastContext = context;
            return Task.FromResult(ReplCommandResult.Continue("handled"));
        }
    }
}
