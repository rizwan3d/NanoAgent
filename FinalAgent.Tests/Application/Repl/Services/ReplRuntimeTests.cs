using FinalAgent.Application.Abstractions;
using FinalAgent.Application.Models;
using FinalAgent.Application.Repl.Services;
using FinalAgent.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinalAgent.Tests.Application.Repl.Services;

public sealed class ReplRuntimeTests
{
    private static readonly ReplSessionContext Session = new(
        new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
        "gpt-oss-20b");

    [Fact]
    public async Task RunAsync_Should_DispatchSlashCommand_When_InputStartsWithSlash()
    {
        QueueReplInputReader inputReader = new("/help", "/exit");
        RecordingReplOutputWriter outputWriter = new();

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .SetupSequence(dispatcher => dispatcher.DispatchAsync(
                It.IsAny<string>(),
                Session,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Continue("Available commands"))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            commandDispatcher.Object,
            conversationPipeline.Object);

        await sut.RunAsync(Session, CancellationToken.None);

        commandDispatcher.Verify(dispatcher => dispatcher.DispatchAsync("/help", Session, It.IsAny<CancellationToken>()), Times.Once);
        commandDispatcher.Verify(dispatcher => dispatcher.DispatchAsync("/exit", Session, It.IsAny<CancellationToken>()), Times.Once);
        conversationPipeline.VerifyNoOtherCalls();
        outputWriter.InfoMessages.Should().Contain(message => message.Contains("Shell ready."));
        outputWriter.InfoMessages.Should().Contain("Available commands");
    }

    [Fact]
    public async Task RunAsync_Should_IgnoreWhitespaceOnlyInput_When_LineContainsNoContent()
    {
        QueueReplInputReader inputReader = new("   ", "/exit");
        RecordingReplOutputWriter outputWriter = new();

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync("/exit", Session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            commandDispatcher.Object,
            conversationPipeline.Object);

        await sut.RunAsync(Session, CancellationToken.None);

        commandDispatcher.Verify(dispatcher => dispatcher.DispatchAsync("/exit", Session, It.IsAny<CancellationToken>()), Times.Once);
        conversationPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_Should_SendNonCommandInputToConversationPipeline_When_LineDoesNotStartWithSlash()
    {
        QueueReplInputReader inputReader = new("help me plan this change", "/exit");
        RecordingReplOutputWriter outputWriter = new();

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync("/exit", Session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                "help me plan this change",
                Session,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationTurnResult("Response ready"));

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            commandDispatcher.Object,
            conversationPipeline.Object);

        await sut.RunAsync(Session, CancellationToken.None);

        conversationPipeline.Verify(pipeline => pipeline.ProcessAsync(
            "help me plan this change",
            Session,
            It.IsAny<CancellationToken>()), Times.Once);
        outputWriter.Responses.Should().ContainSingle().Which.Should().Be("Response ready");
    }

    [Fact]
    public async Task RunAsync_Should_ContinueAfterCommandError_When_DispatcherThrows()
    {
        QueueReplInputReader inputReader = new("/help", "/exit");
        RecordingReplOutputWriter outputWriter = new();

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .SetupSequence(dispatcher => dispatcher.DispatchAsync(
                It.IsAny<string>(),
                Session,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            commandDispatcher.Object,
            conversationPipeline.Object);

        await sut.RunAsync(Session, CancellationToken.None);

        outputWriter.ErrorMessages.Should().ContainSingle(message =>
            message.Contains("command failed unexpectedly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_Should_ContinueAfterConversationError_When_PipelineThrows()
    {
        QueueReplInputReader inputReader = new("hello", "/exit");
        RecordingReplOutputWriter outputWriter = new();

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync("/exit", Session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync("hello", Session, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            commandDispatcher.Object,
            conversationPipeline.Object);

        await sut.RunAsync(Session, CancellationToken.None);

        outputWriter.ErrorMessages.Should().ContainSingle(message =>
            message.Contains("conversation pipeline failed unexpectedly", StringComparison.OrdinalIgnoreCase));
        commandDispatcher.Verify(dispatcher => dispatcher.DispatchAsync("/exit", Session, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ReplRuntime CreateSut(
        IReplInputReader inputReader,
        IReplOutputWriter outputWriter,
        IReplCommandDispatcher commandDispatcher,
        IConversationPipeline conversationPipeline)
    {
        return new ReplRuntime(
            inputReader,
            outputWriter,
            commandDispatcher,
            conversationPipeline,
            NullLogger<ReplRuntime>.Instance);
    }

    private sealed class QueueReplInputReader : IReplInputReader
    {
        private readonly Queue<string?> _inputs;

        public QueueReplInputReader(params string?[] inputs)
        {
            _inputs = new Queue<string?>(inputs);
        }

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_inputs.Count == 0 ? null : _inputs.Dequeue());
        }
    }

    private sealed class RecordingReplOutputWriter : IReplOutputWriter
    {
        public List<string> ErrorMessages { get; } = [];

        public List<string> InfoMessages { get; } = [];

        public List<string> Responses { get; } = [];

        public Task WriteErrorAsync(string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ErrorMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteInfoAsync(string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InfoMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteResponseAsync(string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Responses.Add(message);
            return Task.CompletedTask;
        }
    }
}
