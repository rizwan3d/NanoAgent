using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Services;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Moq;

namespace NanoAgent.Tests.Application.Tools;

public sealed class AgentDelegateToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_RunRequestedSubagentAndReturnHandoff()
    {
        ReplSessionContext parentSession = CreateSession(BuiltInAgentProfiles.Build);
        ReplSessionContext? childSession = null;
        string? delegatedInput = null;

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                It.IsAny<string>(),
                It.IsAny<ReplSessionContext>(),
                It.IsAny<IConversationProgressSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ReplSessionContext, IConversationProgressSink, CancellationToken>((input, session, _, _) =>
            {
                delegatedInput = input;
                childSession = session;

                ToolExecutionBatchResult toolResult = new([
                    new ToolInvocationResult(
                        "call_1",
                        AgentToolNames.FileRead,
                        ToolResultFactory.Success(
                            "Read file.",
                            new ToolErrorPayload("ok", "ok"),
                            ToolJsonContext.Default.ToolErrorPayload))
                ]);

                return Task.FromResult(ConversationTurnResult.AssistantMessage(
                    "Parser lives in ReplCommandParser.cs.",
                    toolResult,
                    new ConversationTurnMetrics(TimeSpan.FromSeconds(2), 7)));
            });

        using ServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(conversationPipeline.Object)
            .BuildServiceProvider();
        AgentDelegateTool sut = new(
            serviceProvider,
            new BuiltInAgentProfileResolver(),
            new HeuristicTokenEstimator());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                parentSession,
                """{ "agent": "explore", "task": "Find the parser", "context": "Focus on REPL commands." }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        AgentDelegationResult payload = JsonSerializer.Deserialize(
            result.JsonResult,
            ToolJsonContext.Default.AgentDelegationResult)!;
        payload.AgentName.Should().Be(BuiltInAgentProfiles.ExploreName);
        payload.Task.Should().Be("Find the parser");
        payload.Response.Should().Be("Parser lives in ReplCommandParser.cs.");
        payload.ExecutedTools.Should().Equal(AgentToolNames.FileRead);
        payload.EstimatedOutputTokens.Should().Be(7);
        payload.RecordedFileEdits.Should().BeFalse();

        childSession.Should().NotBeNull();
        childSession!.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.ExploreName);
        delegatedInput.Should().Contain("Delegated task from parent agent 'build'");
        delegatedInput.Should().Contain("Find the parser");
        delegatedInput.Should().Contain("Focus on REPL commands.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_DenyEditingSubagent_When_ParentProfileIsReadOnly()
    {
        ReplSessionContext parentSession = CreateSession(BuiltInAgentProfiles.Plan);
        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);

        using ServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(conversationPipeline.Object)
            .BuildServiceProvider();
        AgentDelegateTool sut = new(
            serviceProvider,
            new BuiltInAgentProfileResolver(),
            new HeuristicTokenEstimator());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                parentSession,
                """{ "agent": "general", "task": "Implement a fix" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.PermissionDenied);
        result.JsonResult.Should().Contain("readonly_profile_cannot_delegate_edits");
        conversationPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_Should_RecordChildFileEditsOnParentUndoStack()
    {
        ReplSessionContext parentSession = CreateSession(BuiltInAgentProfiles.Build);

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                It.IsAny<string>(),
                It.IsAny<ReplSessionContext>(),
                It.IsAny<IConversationProgressSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ReplSessionContext, IConversationProgressSink, CancellationToken>((_, childSession, _, _) =>
            {
                childSession.RecordFileEditTransaction(new WorkspaceFileEditTransaction(
                    "child edit",
                    [new WorkspaceFileEditState("src/app.cs", true, "before")],
                    [new WorkspaceFileEditState("src/app.cs", true, "after")]));

                return Task.FromResult(ConversationTurnResult.AssistantMessage("Implemented the focused fix."));
            });

        using ServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(conversationPipeline.Object)
            .BuildServiceProvider();
        AgentDelegateTool sut = new(
            serviceProvider,
            new BuiltInAgentProfileResolver(),
            new HeuristicTokenEstimator());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                parentSession,
                """{ "agent": "general", "task": "Implement a focused fix" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        parentSession.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction)
            .Should()
            .BeTrue();
        transaction.Should().NotBeNull();
        transaction!.Description.Should().Be("subagent general: Implement a focused fix");
        transaction.BeforeStates.Should().ContainSingle().Which.Content.Should().Be("before");
        transaction.AfterStates.Should().ContainSingle().Which.Content.Should().Be("after");

        AgentDelegationResult payload = JsonSerializer.Deserialize(
            result.JsonResult,
            ToolJsonContext.Default.AgentDelegationResult)!;
        payload.RecordedFileEdits.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_ProfileIsNotSubagent()
    {
        ReplSessionContext parentSession = CreateSession(BuiltInAgentProfiles.Build);
        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);

        using ServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(conversationPipeline.Object)
            .BuildServiceProvider();
        AgentDelegateTool sut = new(
            serviceProvider,
            new BuiltInAgentProfileResolver(),
            new HeuristicTokenEstimator());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                parentSession,
                """{ "agent": "build", "task": "Do work" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.JsonResult.Should().Contain("profile_is_not_subagent");
        conversationPipeline.VerifyNoOtherCalls();
    }

    private static ToolExecutionContext CreateContext(
        ReplSessionContext session,
        string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_agent",
            AgentToolNames.AgentDelegate,
            document.RootElement.Clone(),
            session);
    }

    private static ReplSessionContext CreateSession(IAgentProfile profile)
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini", "gpt-4.1"],
            agentProfile: profile);
    }
}
