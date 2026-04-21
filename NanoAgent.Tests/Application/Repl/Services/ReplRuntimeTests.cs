using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Repl.Services;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Tests.Application.Repl.Services;

public sealed class ReplRuntimeTests
{
    [Fact]
    public async Task RunAsync_Should_DispatchSlashCommand_When_InputStartsWithSlash()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("/help", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand helpCommand = new("/help", "help", string.Empty, []);
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/help")).Returns(helpCommand);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .SetupSequence(dispatcher => dispatcher.DispatchAsync(
                It.IsAny<ParsedReplCommand>(),
                session,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Continue("Available commands"))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            Mock.Of<ITokenEstimator>());

        await sut.RunAsync(session, CancellationToken.None);

        commandDispatcher.Verify(dispatcher => dispatcher.DispatchAsync(helpCommand, session, It.IsAny<CancellationToken>()), Times.Once);
        commandDispatcher.Verify(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()), Times.Once);
        conversationPipeline.VerifyNoOtherCalls();
        outputWriter.HeaderMessages.Should().ContainSingle().Which.Should().Be("NanoAgent|gpt-oss-20b");
        outputWriter.InfoMessages.Should().Contain("Available commands");
    }

    [Fact]
    public async Task RunAsync_Should_DispatchSlashCommand_When_FirstRedirectedLineContainsUtf8Bom()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("\uFEFF/help", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand helpCommand = new("/help", "help", string.Empty, []);
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/help")).Returns(helpCommand);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .SetupSequence(dispatcher => dispatcher.DispatchAsync(
                It.IsAny<ParsedReplCommand>(),
                session,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Continue("Available commands"))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            Mock.Of<ITokenEstimator>());

        await sut.RunAsync(session, CancellationToken.None);

        commandDispatcher.Verify(dispatcher => dispatcher.DispatchAsync(helpCommand, session, It.IsAny<CancellationToken>()), Times.Once);
        conversationPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_Should_DispatchSlashCommand_When_FirstRedirectedLineContainsUtf8BomMojibakePrefix()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("\u00EF\u00BB\u00BF/help", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand helpCommand = new("/help", "help", string.Empty, []);
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/help")).Returns(helpCommand);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .SetupSequence(dispatcher => dispatcher.DispatchAsync(
                It.IsAny<ParsedReplCommand>(),
                session,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Continue("Available commands"))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            Mock.Of<ITokenEstimator>());

        await sut.RunAsync(session, CancellationToken.None);

        commandDispatcher.Verify(dispatcher => dispatcher.DispatchAsync(helpCommand, session, It.IsAny<CancellationToken>()), Times.Once);
        conversationPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_Should_IgnoreWhitespaceOnlyInput_When_LineContainsNoContent()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("   ", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            Mock.Of<ITokenEstimator>());

        await sut.RunAsync(session, CancellationToken.None);

        commandDispatcher.Verify(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()), Times.Once);
        conversationPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_Should_SendNonCommandInputToConversationPipeline_When_LineDoesNotStartWithSlash()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("help me plan this change", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                "help me plan this change",
                session,
                It.IsAny<IConversationProgressSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationTurnResult.AssistantMessage(
                "Response ready",
                new ConversationTurnMetrics(TimeSpan.FromSeconds(4), 14)));

        Mock<ITokenEstimator> tokenEstimator = new(MockBehavior.Strict);
        tokenEstimator.Setup(estimator => estimator.Estimate("help me plan this change")).Returns(5);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);
        replSectionService
            .Setup(service => service.EnsureTitleGenerationStarted(session, "help me plan this change"));
        replSectionService
            .Setup(service => service.SaveIfDirtyAsync(session, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            tokenEstimator.Object);

        await sut.RunAsync(session, CancellationToken.None);

        conversationPipeline.Verify(pipeline => pipeline.ProcessAsync(
            "help me plan this change",
            session,
            It.IsAny<IConversationProgressSink>(),
            It.IsAny<CancellationToken>()), Times.Once);
        outputWriter.ProgressStarts.Should().ContainSingle().Which.Should().Be((5, 0));
        outputWriter.Responses.Should().ContainSingle().Which.Should().Be("Response ready");
        outputWriter.ResponseMetrics.Should().ContainSingle().Which.Should().Be("(4s \u00B7 14 tokens est.)");
        session.TotalEstimatedOutputTokens.Should().Be(14);
    }

    [Fact]
    public async Task RunAsync_Should_ShowToolCallsInRealtimeBeforeAssistantResponse_When_ConversationReportsProgress()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("create the app", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        ToolExecutionBatchResult toolExecutionResult = new([
            new ToolInvocationResult(
                "call_1",
                "directory_list",
                ToolResultFactory.Success(
                    "Listed the directory.",
                    new ToolErrorPayload("ok", "ok"),
                    ToolJsonContext.Default.ToolErrorPayload,
                    new ToolRenderPayload("Directory listing: .", "file: index.html"))),
            new ToolInvocationResult(
                "call_2",
                "file_write",
                ToolResultFactory.Success(
                    "Wrote index.html.",
                    new ToolErrorPayload("ok", "ok"),
                    ToolJsonContext.Default.ToolErrorPayload,
                    new ToolRenderPayload("File written: styles.css", "Created styles.css with 120 characters.")))
        ]);

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                "create the app",
                session,
                It.IsAny<IConversationProgressSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ReplSessionContext, IConversationProgressSink, CancellationToken>(async (_, _, progressSink, cancellationToken) =>
            {
                await progressSink.ReportToolCallsStartedAsync([
                    new ConversationToolCall("call_1", "directory_list", "{}"),
                    new ConversationToolCall("call_2", "file_write", """{"path":"index.html"}""")
                ], cancellationToken);

                await progressSink.ReportToolResultsAsync(
                    toolExecutionResult,
                    cancellationToken);

                return ConversationTurnResult.AssistantMessage(
                    "Created the requested files.",
                    toolExecutionResult,
                    new ConversationTurnMetrics(TimeSpan.FromSeconds(3), 12));
            });

        Mock<ITokenEstimator> tokenEstimator = new(MockBehavior.Strict);
        tokenEstimator.Setup(estimator => estimator.Estimate("create the app")).Returns(4);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);
        replSectionService
            .Setup(service => service.EnsureTitleGenerationStarted(session, "create the app"));
        replSectionService
            .Setup(service => service.SaveIfDirtyAsync(session, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            tokenEstimator.Object);

        await sut.RunAsync(session, CancellationToken.None);

        outputWriter.ToolCalls.Should().ContainInOrder("directory_list", "file_write");
        outputWriter.Events.Should().ContainInOrder(
            "tool:directory_list",
            "tool:file_write",
            "tool-output:Directory listing: .",
            "tool-output:File written: styles.css",
            "response:Created the requested files.");
        outputWriter.Responses.Should().ContainSingle().Which.Should().Be("Created the requested files.");
        outputWriter.ToolOutputs.Should().ContainInOrder(
            "Directory listing: .\nfile: index.html",
            "File written: styles.css\nCreated styles.css with 120 characters.");
    }

    [Fact]
    public async Task RunAsync_Should_KeepCumulativeTokenCountAcrossResponses_When_MultipleTurnsComplete()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("first question", "second question", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .SetupSequence(pipeline => pipeline.ProcessAsync(
                It.IsAny<string>(),
                session,
                It.IsAny<IConversationProgressSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationTurnResult.AssistantMessage(
                "First response",
                new ConversationTurnMetrics(TimeSpan.FromSeconds(2), 14)))
            .ReturnsAsync(ConversationTurnResult.AssistantMessage(
                "Second response",
                new ConversationTurnMetrics(TimeSpan.FromSeconds(3), 12)));

        Mock<ITokenEstimator> tokenEstimator = new(MockBehavior.Strict);
        tokenEstimator.Setup(estimator => estimator.Estimate("first question")).Returns(5);
        tokenEstimator.Setup(estimator => estimator.Estimate("second question")).Returns(3);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);
        replSectionService
            .Setup(service => service.EnsureTitleGenerationStarted(session, "first question"));
        replSectionService
            .Setup(service => service.EnsureTitleGenerationStarted(session, "second question"));
        replSectionService
            .Setup(service => service.SaveIfDirtyAsync(session, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            tokenEstimator.Object);

        await sut.RunAsync(session, CancellationToken.None);

        outputWriter.ProgressStarts.Should().ContainInOrder((5, 0), (3, 14));
        outputWriter.ResponseMetrics.Should().ContainInOrder(
            "(2s \u00B7 14 tokens est.)",
            "(3s \u00B7 26 tokens est.)");
        session.TotalEstimatedOutputTokens.Should().Be(26);
    }

    [Fact]
    public async Task RunAsync_Should_ContinueAfterCommandError_When_DispatcherThrows()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("/help", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand helpCommand = new("/help", "help", string.Empty, []);
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/help")).Returns(helpCommand);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .SetupSequence(dispatcher => dispatcher.DispatchAsync(
                It.IsAny<ParsedReplCommand>(),
                session,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            Mock.Of<ITokenEstimator>());

        await sut.RunAsync(session, CancellationToken.None);

        outputWriter.ErrorMessages.Should().ContainSingle(message =>
            message.Contains("command failed unexpectedly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_Should_ContinueAfterConversationIsInterrupted_When_RequestIsCancelled()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("hello", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync("hello", session, It.IsAny<IConversationProgressSink>(), It.IsAny<CancellationToken>()))
            .Returns<string, ReplSessionContext, IConversationProgressSink, CancellationToken>(static async (_, _, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return ConversationTurnResult.AssistantMessage("should not happen");
            });

        Mock<ITokenEstimator> tokenEstimator = new(MockBehavior.Strict);
        tokenEstimator.Setup(estimator => estimator.Estimate("hello")).Returns(2);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);
        replSectionService
            .Setup(service => service.EnsureTitleGenerationStarted(session, "hello"));

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new ScheduledInterruptMonitor(TimeSpan.FromMilliseconds(25)),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            tokenEstimator.Object);

        await sut.RunAsync(session, CancellationToken.None);

        outputWriter.WarningMessages.Should().ContainSingle(message =>
            message.Contains("Interrupted the current request", StringComparison.OrdinalIgnoreCase));
        outputWriter.ErrorMessages.Should().BeEmpty();
        outputWriter.Responses.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_Should_ContinueAfterConversationError_When_PipelineThrows()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("hello", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync("hello", session, It.IsAny<IConversationProgressSink>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        Mock<ITokenEstimator> tokenEstimator = new(MockBehavior.Strict);
        tokenEstimator.Setup(estimator => estimator.Estimate("hello")).Returns(2);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);
        replSectionService
            .Setup(service => service.EnsureTitleGenerationStarted(session, "hello"));

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            tokenEstimator.Object);

        await sut.RunAsync(session, CancellationToken.None);

        outputWriter.ProgressStarts.Should().ContainSingle().Which.Should().Be((2, 0));
        outputWriter.ErrorMessages.Should().ContainSingle(message =>
            message.Contains("conversation pipeline failed unexpectedly", StringComparison.OrdinalIgnoreCase));
        commandDispatcher.Verify(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_Should_ShowSpecificConversationError_When_PipelineThrowsKnownConversationException()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("hello", "/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync("hello", session, It.IsAny<IConversationProgressSink>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConversationResponseException("The provider returned neither assistant content, a refusal, nor usable tool calls."));

        Mock<ITokenEstimator> tokenEstimator = new(MockBehavior.Strict);
        tokenEstimator.Setup(estimator => estimator.Estimate("hello")).Returns(2);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);
        replSectionService
            .Setup(service => service.EnsureTitleGenerationStarted(session, "hello"));

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            tokenEstimator.Object);

        await sut.RunAsync(session, CancellationToken.None);

        outputWriter.ErrorMessages.Should().ContainSingle(message =>
            message.Contains("neither assistant content, a refusal, nor usable tool calls", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_Should_WriteResumeCommand_When_ExitCommandEndsTheSession()
    {
        ReplSessionContext session = CreateSession();
        QueueReplInputReader inputReader = new("/exit");
        RecordingReplOutputWriter outputWriter = new();
        ParsedReplCommand exitCommand = new("/exit", "exit", string.Empty, []);

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        commandParser.Setup(parser => parser.Parse("/exit")).Returns(exitCommand);

        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        commandDispatcher
            .Setup(dispatcher => dispatcher.DispatchAsync(exitCommand, session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReplCommandResult.Exit());

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            Mock.Of<ITokenEstimator>());

        await sut.RunAsync(session, CancellationToken.None);

        outputWriter.InfoMessages.Should().Contain(message =>
            message.Contains(session.SectionResumeCommand, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_Should_WriteResumeCommand_When_HostCancellationStopsTheSession()
    {
        ReplSessionContext session = CreateSession();
        BlockingReplInputReader inputReader = new();
        RecordingReplOutputWriter outputWriter = new();

        Mock<IReplCommandParser> commandParser = new(MockBehavior.Strict);
        Mock<IReplCommandDispatcher> commandDispatcher = new(MockBehavior.Strict);
        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        Mock<IReplSectionService> replSectionService = CreateSectionServiceMock(session);

        ReplRuntime sut = CreateSut(
            inputReader,
            outputWriter,
            new NoOpReplInterruptMonitor(),
            commandParser.Object,
            commandDispatcher.Object,
            conversationPipeline.Object,
            replSectionService.Object,
            Mock.Of<ITokenEstimator>());

        using CancellationTokenSource cancellationSource = new();
        Task runTask = sut.RunAsync(session, cancellationSource.Token);
        await Task.Delay(25);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

        outputWriter.InfoMessages.Should().Contain(message =>
            message.Contains(session.SectionResumeCommand, StringComparison.Ordinal));
    }

    private static ReplRuntime CreateSut(
        IReplInputReader inputReader,
        IReplOutputWriter outputWriter,
        IReplInterruptMonitor interruptMonitor,
        IReplCommandParser commandParser,
        IReplCommandDispatcher commandDispatcher,
        IConversationPipeline conversationPipeline,
        IReplSectionService replSectionService,
        ITokenEstimator tokenEstimator)
    {
        return new ReplRuntime(
            inputReader,
            outputWriter,
            interruptMonitor,
            commandParser,
            commandDispatcher,
            conversationPipeline,
            replSectionService,
            tokenEstimator,
            NullLogger<ReplRuntime>.Instance);
    }

    private static Mock<IReplSectionService> CreateSectionServiceMock(ReplSessionContext session)
    {
        Mock<IReplSectionService> replSectionService = new(MockBehavior.Strict);
        replSectionService
            .Setup(service => service.EnsureTitleGenerationStarted(session, It.IsAny<string>()));
        replSectionService
            .Setup(service => service.SaveIfDirtyAsync(session, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        replSectionService
            .Setup(service => service.StopAsync(session, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return replSectionService;
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            "NanoAgent",
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-oss-20b",
            ["gpt-oss-20b", "qwen/qwen3-coder-30b"]);
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

    private sealed class BlockingReplInputReader : IReplInputReader
    {
        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class NoOpReplInterruptMonitor : IReplInterruptMonitor
    {
        public ValueTask<IAsyncDisposable> StartMonitoringAsync(
            CancellationTokenSource requestCancellationSource,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IAsyncDisposable>(NoOpAsyncDisposable.Instance);
        }
    }

    private sealed class ScheduledInterruptMonitor : IReplInterruptMonitor
    {
        private readonly TimeSpan _delay;

        public ScheduledInterruptMonitor(TimeSpan delay)
        {
            _delay = delay;
        }

        public ValueTask<IAsyncDisposable> StartMonitoringAsync(
            CancellationTokenSource requestCancellationSource,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task task = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_delay, cancellationToken);
                    requestCancellationSource.Cancel();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }, cancellationToken);

            return ValueTask.FromResult<IAsyncDisposable>(new TaskBackedAsyncDisposable(task));
        }
    }

    private sealed class TaskBackedAsyncDisposable : IAsyncDisposable
    {
        private readonly Task _task;

        public TaskBackedAsyncDisposable(Task task)
        {
            _task = task;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _task;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static NoOpAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingReplOutputWriter : IReplOutputWriter
    {
        public List<string> ErrorMessages { get; } = [];

        public List<string> HeaderMessages { get; } = [];

        public List<string> InfoMessages { get; } = [];

        public List<(int EstimatedOutputTokens, int CompletedSessionEstimatedOutputTokens)> ProgressStarts { get; } = [];

        public List<string> Responses { get; } = [];

        public List<string> ResponseMetrics { get; } = [];

        public List<string> ToolCalls { get; } = [];

        public List<string> ToolOutputs { get; } = [];

        public List<string> Events { get; } = [];

        public List<string> WarningMessages { get; } = [];

        public ValueTask<IResponseProgress> BeginResponseProgressAsync(
            int estimatedOutputTokens,
            int completedSessionEstimatedOutputTokens,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProgressStarts.Add((estimatedOutputTokens, completedSessionEstimatedOutputTokens));
            return ValueTask.FromResult<IResponseProgress>(new RecordingResponseProgress(this));
        }

        public Task WriteErrorAsync(string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ErrorMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteShellHeaderAsync(
            string applicationName,
            string modelName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HeaderMessages.Add($"{applicationName}|{modelName}");
            return Task.CompletedTask;
        }

        public Task WriteInfoAsync(string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InfoMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteWarningAsync(string message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WarningMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteResponseAsync(
            string message,
            ConversationTurnMetrics? metrics,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Responses.Add(message);
            Events.Add($"response:{message}");

            if (metrics is not null)
            {
                ResponseMetrics.Add(metrics.ToDisplayText());
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingResponseProgress : IResponseProgress
    {
        private readonly RecordingReplOutputWriter _owner;

        public RecordingResponseProgress(RecordingReplOutputWriter owner)
        {
            _owner = owner;
        }

        public Task ReportExecutionPlanAsync(
            ExecutionPlanProgress executionPlanProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _owner.Events.Add($"plan:{executionPlanProgress.CompletedTaskCount}/{executionPlanProgress.Tasks.Count}");
            return Task.CompletedTask;
        }

        public Task ReportToolCallsStartedAsync(
            IReadOnlyList<ConversationToolCall> toolCalls,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (ConversationToolCall toolCall in toolCalls)
            {
                _owner.ToolCalls.Add(toolCall.Name);
                _owner.Events.Add($"tool:{toolCall.Name}");
            }

            return Task.CompletedTask;
        }

        public Task ReportToolResultsAsync(
            ToolExecutionBatchResult toolExecutionResult,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (ToolInvocationResult invocationResult in toolExecutionResult.Results)
            {
                string output = invocationResult.ToDisplayText()
                    .Replace("\r\n", "\n", StringComparison.Ordinal);

                _owner.ToolOutputs.Add(output);
                _owner.Events.Add($"tool-output:{output.Split('\n')[0]}");
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
