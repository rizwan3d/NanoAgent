using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Services;

public sealed class AgentTurnServiceTests
{
    [Fact]
    public async Task RunTurnAsync_Should_RunNormalPromptThroughConversationPipeline()
    {
        ReplSessionContext session = CreateSession();
        RecordingProgressSink progressSink = new();

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                "inspect this",
                session,
                progressSink,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationTurnResult.AssistantMessage("Done."));

        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver());

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "inspect this", progressSink),
            CancellationToken.None);

        result.ResponseText.Should().Be("Done.");
        session.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.BuildName);
        conversationPipeline.VerifyAll();
    }

    [Fact]
    public async Task RunTurnAsync_Should_InvokeMentionedSubagentForOneTurnAndRestoreProfile()
    {
        ReplSessionContext session = CreateSession();
        RecordingProgressSink progressSink = new();

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                "find the parser",
                session,
                progressSink,
                It.IsAny<CancellationToken>()))
            .Returns<string, ReplSessionContext, IConversationProgressSink, CancellationToken>((_, activeSession, _, _) =>
            {
                activeSession.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.ExploreName);
                return Task.FromResult(ConversationTurnResult.AssistantMessage("Parser is in ReplCommandParser.cs."));
            });

        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver());

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "@explore find the parser", progressSink),
            CancellationToken.None);

        result.ResponseText.Should().Be("Parser is in ReplCommandParser.cs.");
        session.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.BuildName);
        conversationPipeline.VerifyAll();
    }

    [Fact]
    public async Task RunTurnAsync_Should_ReturnHelpfulMessage_When_MentionedAgentIsPrimary()
    {
        ReplSessionContext session = CreateSession();
        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);

        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver());

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "@plan inspect this", new RecordingProgressSink()),
            CancellationToken.None);

        result.ResponseText.Should().Contain("primary profile");
        result.ResponseText.Should().Contain("/profile plan");
        conversationPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunTurnAsync_Should_ReturnHelpfulMessage_When_SubagentTaskIsMissing()
    {
        ReplSessionContext session = CreateSession();
        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);

        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver());

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "@general", new RecordingProgressSink()),
            CancellationToken.None);

        result.ResponseText.Should().Contain("Tell '@general' what to do");
        conversationPipeline.VerifyNoOtherCalls();
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"]);
    }

    private sealed class RecordingProgressSink : IConversationProgressSink
    {
        public Task ReportExecutionPlanAsync(
            ExecutionPlanProgress executionPlanProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ReportToolCallsStartedAsync(
            IReadOnlyList<ConversationToolCall> toolCalls,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ReportToolResultsAsync(
            ToolExecutionBatchResult toolExecutionResult,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
