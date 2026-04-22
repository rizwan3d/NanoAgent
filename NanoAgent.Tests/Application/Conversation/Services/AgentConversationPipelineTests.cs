using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Conversation.Services;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Services;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace NanoAgent.Tests.Application.Conversation.Services;

public sealed class AgentConversationPipelineTests
{
    [Fact]
    public async Task ProcessAsync_Should_RunSingleConversationPass_When_ResponseContainsNormalAssistantContent()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings("Base prompt"));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([
                CreateToolDefinition(AgentToolNames.PlanningMode),
                CreateToolDefinition(AgentToolNames.FileRead),
                CreateToolDefinition(AgentToolNames.FileWrite),
                CreateToolDefinition(AgentToolNames.ShellCommand)
            ]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    "resp_1"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .Setup(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                "Implemented the refactor.",
                [],
                "resp_1"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            toolExecutionPipeline.Object,
            toolRegistry.Object,
            configurationAccessor.Object);

        ConversationTurnResult result = await sut.ProcessAsync(
            "Implement the next refactor.",
            session,
            CancellationToken.None);

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ResponseText.Should().Be("Implemented the refactor.");
        result.Metrics.Should().NotBeNull();
        requests.Should().HaveCount(1);
        requests[0].SystemPrompt.Should().Contain("Base prompt");
        requests[0].SystemPrompt.Should().Contain("Active agent profile: build.");
        requests[0].SystemPrompt.Should().Contain("planning_mode");
        requests[0].SystemPrompt.Should().Contain("installed build tools");
        requests[0].SystemPrompt.Should().Contain("separate verified facts from assumptions or open questions");
        requests[0].SystemPrompt.Should().Contain("Verified facts, Assumptions / open questions");
        requests[0].SystemPrompt.Should().Contain("compare approaches");
        requests[0].SystemPrompt.Should().Contain("Avoid low-quality plans such as");
        requests[0].SystemPrompt.Should().Contain("npm create vite@latest");
        requests[0].SystemPrompt.Should().Contain("fully specified, non-interactive commands");
        requests[0].SystemPrompt.Should().Contain("one task at a time");
        requests[0].SystemPrompt.Should().Contain("finish the requested implementation when practical");
        requests[0].SystemPrompt.Should().Contain("do not stop at analysis if you can safely continue");
        requests[0].SystemPrompt.Should().NotContain("You are NanoAgent in Planning Mode.");
        requests[0].SystemPrompt.Should().NotContain("EXECUTION PHASE IS ACTIVE.");
        requests[0].AvailableTools.Select(static tool => tool.Name)
            .Should()
            .Equal(
                AgentToolNames.PlanningMode,
                AgentToolNames.FileRead,
                AgentToolNames.FileWrite,
                AgentToolNames.ShellCommand);
        requests[0].Messages.Should().HaveCount(1);
        requests[0].Messages[0].Role.Should().Be("user");
        requests[0].Messages[0].Content.Should().Be("Implement the next refactor.");
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[0].Content.Should().Be("Implement the next refactor.");
        session.ConversationHistory[1].Content.Should().Be("Implemented the refactor.");
        session.PendingExecutionPlan.Should().BeNull();
        toolExecutionPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_Should_PassThinkingEffortToProviderRequest()
    {
        ReplSessionContext session = CreateSession();
        session.SetReasoningEffort("high");
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings());

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    "resp_1"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .Setup(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                "Done.",
                [],
                "resp_1"));

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            Mock.Of<IToolExecutionPipeline>(),
            toolRegistry.Object,
            configurationAccessor.Object);

        await sut.ProcessAsync(
            "Use deeper thinking.",
            session,
            CancellationToken.None);

        requests.Should().ContainSingle();
        requests[0].ReasoningEffort.Should().Be("high");
    }

    [Fact]
    public async Task ProcessAsync_Should_FilterAvailableTools_When_ProfileIsReadOnly()
    {
        ReplSessionContext session = CreateSession(BuiltInAgentProfiles.Plan);
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings("Base prompt"));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([
                CreateToolDefinition(AgentToolNames.ApplyPatch),
                CreateToolDefinition(AgentToolNames.DirectoryList),
                CreateToolDefinition(AgentToolNames.FileRead),
                CreateToolDefinition(AgentToolNames.FileWrite),
                CreateToolDefinition(AgentToolNames.ShellCommand),
                CreateToolDefinition(AgentToolNames.TextSearch)
            ]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    "resp_1"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .Setup(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                "Here is the plan.",
                [],
                "resp_1"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            toolExecutionPipeline.Object,
            toolRegistry.Object,
            configurationAccessor.Object);

        await sut.ProcessAsync(
            "Plan this safely.",
            session,
            CancellationToken.None);

        requests.Should().ContainSingle();
        requests[0].SystemPrompt.Should().Contain("Active agent profile: plan.");
        requests[0].SystemPrompt.Should().Contain("evidence-based implementation plan");
        requests[0].SystemPrompt.Should().Contain("Do not patch, write files, install dependencies");
        requests[0].AvailableTools.Select(static tool => tool.Name)
            .Should()
            .Equal(
                AgentToolNames.DirectoryList,
                AgentToolNames.FileRead,
                AgentToolNames.ShellCommand,
                AgentToolNames.TextSearch);
        requests[0].AvailableTools.Select(static tool => tool.Name)
            .Should()
            .NotContain([AgentToolNames.ApplyPatch, AgentToolNames.FileWrite]);
    }

    [Fact]
    public async Task ProcessAsync_Should_RetryEmptyStopResponseOnce_When_MapperMarksResponseRetryable()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings("Base prompt"));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([CreateToolDefinition(AgentToolNames.PlanningMode)]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    $"resp_{requests.Count}"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .SetupSequence(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Throws(new ConversationResponseException(
                "The provider returned neither assistant content, a refusal, nor usable tool calls. Finish reason: stop.",
                isRetryableEmptyResponse: true))
            .Returns(new ConversationResponse(
                "Implemented the refactor.",
                [],
                "resp_2"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            toolExecutionPipeline.Object,
            toolRegistry.Object,
            configurationAccessor.Object);

        ConversationTurnResult result = await sut.ProcessAsync(
            "Implement the next refactor.",
            session,
            CancellationToken.None);

        result.ResponseText.Should().Be("Implemented the refactor.");
        requests.Should().HaveCount(2);
        requests[0].SystemPrompt.Should().NotContain("previous provider response was empty");
        requests[1].SystemPrompt.Should().Contain("previous provider response was empty");
        requests[1].SystemPrompt.Should().Contain("materially advances the work");
        requests[1].SystemPrompt.Should().Contain("Do not return empty content");
        requests[1].SystemPrompt.Should().Contain("planning_mode");
        requests[1].Messages.Should().HaveCount(1);
        requests[1].Messages[0].Content.Should().Be("Implement the next refactor.");
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[1].Content.Should().Be("Implemented the refactor.");
        toolExecutionPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_Should_ReturnFallbackMessage_When_EmptyStopRetryAlsoReturnsEmpty()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings("Base prompt"));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([CreateToolDefinition(AgentToolNames.PlanningMode)]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    $"resp_{requests.Count}"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .SetupSequence(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Throws(new ConversationResponseException(
                "The provider returned neither assistant content, a refusal, nor usable tool calls. Finish reason: stop.",
                isRetryableEmptyResponse: true))
            .Throws(new ConversationResponseException(
                "The provider returned neither assistant content, a refusal, nor usable tool calls. Finish reason: stop.",
                isRetryableEmptyResponse: true));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            toolExecutionPipeline.Object,
            toolRegistry.Object,
            configurationAccessor.Object);

        ConversationTurnResult result = await sut.ProcessAsync(
            "Help me with this refactor.",
            session,
            CancellationToken.None);

        result.ResponseText.Should().Contain("did not receive a usable response");
        result.ResponseText.Should().Contain("provider ended normally");
        requests.Should().HaveCount(2);
        requests[1].SystemPrompt.Should().Contain("previous provider response was empty");
        requests[1].SystemPrompt.Should().Contain("another tool-less empty response");
        session.ConversationHistory.Should().BeEmpty();
        session.PendingExecutionPlan.Should().BeNull();
        toolExecutionPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_Should_ReturnPlanResponseWithoutSavingPendingPlan_When_UserRequestsPlan()
    {
        ReplSessionContext session = CreateSession();
        const string planningSummary =
            """
            Objective
            - Plan the refactor first.

            Plan
            1. Inspect the affected files.
            2. Apply the refactor.
            3. Run validation.
            """;

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings("Base prompt"));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([
                CreateToolDefinition(AgentToolNames.PlanningMode),
                CreateToolDefinition(AgentToolNames.FileRead),
                CreateToolDefinition(AgentToolNames.ShellCommand)
            ]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    "resp_1"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .Setup(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                planningSummary,
                [],
                "resp_1"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            toolExecutionPipeline.Object,
            toolRegistry.Object,
            configurationAccessor.Object);

        ConversationTurnResult result = await sut.ProcessAsync(
            "Help me plan this refactor",
            session,
            CancellationToken.None);

        result.ResponseText.Should().Be(planningSummary);
        requests.Should().HaveCount(1);
        requests[0].SystemPrompt.Should().Contain("planning_mode");
        requests[0].SystemPrompt.Should().Contain("immediate next step first");
        requests[0].SystemPrompt.Should().Contain("high-quality ordered task list");
        requests[0].SystemPrompt.Should().Contain("Verified facts, Assumptions / open questions");
        requests[0].SystemPrompt.Should().Contain("recommend the best path");
        requests[0].SystemPrompt.Should().Contain("Avoid low-quality plans such as");
        requests[0].SystemPrompt.Should().Contain("scaffold stays non-interactive");
        requests[0].SystemPrompt.Should().NotContain("You are NanoAgent in Planning Mode.");
        session.PendingExecutionPlan.Should().BeNull();
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[0].Content.Should().Be("Help me plan this refactor");
        session.ConversationHistory[1].Content.Should().Be(planningSummary);
        toolExecutionPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_Should_ExecuteSavedPlan_When_UserApprovesPendingPlan()
    {
        ReplSessionContext session = CreateSession();
        const string planningSummary =
            """
            Objective
            - Plan the refactor first.

            Plan
            1. Inspect the affected files.
            2. Apply the refactor.
            3. Run validation.
            """;

        session.AddConversationTurn("Help me plan this refactor", planningSummary);
        session.SetPendingExecutionPlan(new PendingExecutionPlan(
            "Help me plan this refactor",
            planningSummary,
            [
                "Inspect the affected files.",
                "Apply the refactor.",
                "Run validation."
            ]));

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings("Base prompt"));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([
                CreateToolDefinition(AgentToolNames.PlanningMode),
                CreateToolDefinition(AgentToolNames.FileRead),
                CreateToolDefinition(AgentToolNames.FileWrite),
                CreateToolDefinition(AgentToolNames.ShellCommand)
            ]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    "resp_exec"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .Setup(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                "Implemented the approved plan.",
                [],
                "resp_exec"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            toolExecutionPipeline.Object,
            toolRegistry.Object,
            configurationAccessor.Object);

        ConversationTurnResult result = await sut.ProcessAsync(
            "continue",
            session,
            CancellationToken.None);

        result.ResponseText.Should().Be("Implemented the approved plan.");
        requests.Should().HaveCount(1);
        requests[0].SystemPrompt.Should().Contain("APPROVED EXECUTION PHASE IS ACTIVE.");
        requests[0].SystemPrompt.Should().Contain("one task at a time");
        requests[0].SystemPrompt.Should().Contain("Keep the immediate next step explicit");
        requests[0].SystemPrompt.Should().Contain("revise it deliberately instead of following it blindly");
        requests[0].SystemPrompt.Should().Contain("1. Inspect the affected files.");
        requests[0].SystemPrompt.Should().Contain("use fully specified, non-interactive commands");
        requests[0].Messages.Should().HaveCount(3);
        requests[0].Messages[0].Content.Should().Be("Help me plan this refactor");
        requests[0].Messages[1].Content.Should().Be(planningSummary);
        requests[0].Messages[2].Content.Should().Be("continue");
        session.PendingExecutionPlan.Should().BeNull();
        session.ConversationHistory.Should().HaveCount(4);
        session.ConversationHistory[2].Content.Should().Be("continue");
        session.ConversationHistory[3].Content.Should().Be("Implemented the approved plan.");
        toolExecutionPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_Should_ExecutePlanningModeToolAndWriteToolWithinSingleConversation()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings());

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([
                CreateToolDefinition(AgentToolNames.PlanningMode),
                CreateToolDefinition(AgentToolNames.FileWrite)
            ]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    $"resp_{requests.Count}"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .SetupSequence(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall(
                    "plan_call_1",
                    AgentToolNames.PlanningMode,
                    """{ "objective": "Update the README." }""")],
                "resp_1"))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall(
                    "exec_call_1",
                    AgentToolNames.FileWrite,
                    """{ "path": "README.md", "content": "hello" }""")],
                "resp_2"))
            .Returns(new ConversationResponse(
                "Implemented the requested change.",
                [],
                "resp_3"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.Is<IReadOnlyList<ConversationToolCall>>(calls =>
                    calls.Count == 1 &&
                    calls[0].Name == AgentToolNames.PlanningMode),
                session,
                ConversationExecutionPhase.Execution,
                It.Is<IReadOnlySet<string>>(names =>
                    names.Contains(AgentToolNames.PlanningMode) &&
                    names.Contains(AgentToolNames.FileWrite)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "plan_call_1",
                    AgentToolNames.PlanningMode,
                    ToolResultFactory.Success(
                        "Planning mode activated for 'Update the README.'.",
                        new PlanningModeResult(
                            "Update the README.",
                            [
                                "Inspect relevant files and facts before editing.",
                                "Write a concise plan grounded in the current workspace."
                            ],
                            ["Objective", "Plan"]),
                        ToolJsonContext.Default.PlanningModeResult,
                        new ToolRenderPayload("Planning mode active", "Update the README.")))
            ]));
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.Is<IReadOnlyList<ConversationToolCall>>(calls =>
                    calls.Count == 1 &&
                    calls[0].Name == AgentToolNames.FileWrite),
                session,
                ConversationExecutionPhase.Execution,
                It.Is<IReadOnlySet<string>>(names =>
                    names.Contains(AgentToolNames.PlanningMode) &&
                    names.Contains(AgentToolNames.FileWrite)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "exec_call_1",
                    AgentToolNames.FileWrite,
                    ToolResultFactory.Success(
                        "Created README.md.",
                        new ToolErrorPayload("ok", "ok"),
                        ToolJsonContext.Default.ToolErrorPayload,
                        new ToolRenderPayload("File write complete", "README.md")))
            ]));

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            toolExecutionPipeline.Object,
            toolRegistry.Object,
            configurationAccessor.Object);

        RecordingConversationProgressSink progressSink = new();

        ConversationTurnResult result = await sut.ProcessAsync(
            "Update the README.",
            session,
            progressSink,
            CancellationToken.None);

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ResponseText.Should().Be("Implemented the requested change.");
        result.ToolExecutionResult.Should().NotBeNull();
        result.ToolExecutionResult!.Results.Select(static item => item.ToolName)
            .Should()
            .Equal(AgentToolNames.PlanningMode, AgentToolNames.FileWrite);
        progressSink.PlanProgressUpdates.Should().BeEmpty();
        progressSink.StartedToolBatches.Should().HaveCount(2);
        progressSink.CompletedToolBatches.Should().HaveCount(2);
        requests.Should().HaveCount(3);
        requests[1].Messages.Should().HaveCount(3);
        requests[1].Messages[1].Role.Should().Be("assistant");
        requests[1].Messages[1].ToolCalls.Should().ContainSingle();
        requests[1].Messages[1].ToolCalls[0].Name.Should().Be(AgentToolNames.PlanningMode);
        requests[1].Messages[2].Role.Should().Be("tool");
        requests[2].Messages.Should().HaveCount(5);

        using JsonDocument toolFeedbackDocument = JsonDocument.Parse(requests[2].Messages[^1].Content!);
        JsonElement toolFeedback = toolFeedbackDocument.RootElement;
        toolFeedback.GetProperty("ToolName").GetString().Should().Be(AgentToolNames.FileWrite);
        toolFeedback.GetProperty("Status").GetString().Should().Be("Success");
        toolFeedback.GetProperty("IsSuccess").GetBoolean().Should().BeTrue();
        toolFeedback.GetProperty("Message").GetString().Should().Be("Created README.md.");
        toolFeedback.GetProperty("Render").GetProperty("Title").GetString().Should().Be("File write complete");
        toolFeedback.GetProperty("Data").GetProperty("Code").GetString().Should().Be("ok");
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[1].Content.Should().Be("Implemented the requested change.");
    }

    [Fact]
    public async Task ProcessAsync_Should_ReportLivePlanProgress_When_UpdatePlanToolRuns()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings("Base prompt"));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([
                CreateToolDefinition(AgentToolNames.UpdatePlan),
                CreateToolDefinition(AgentToolNames.FileRead)
            ]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    $"resp_{requests.Count}"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .SetupSequence(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                null,
                [
                    new ConversationToolCall(
                        "plan_call_1",
                        AgentToolNames.UpdatePlan,
                        """
                        {
                          "plan": [
                            { "step": "Inspect planning flow", "status": "completed" },
                            { "step": "Wire live plan progress", "status": "in_progress" },
                            { "step": "Run validation", "status": "pending" }
                          ]
                        }
                        """)
                ],
                "resp_1"))
            .Returns(new ConversationResponse(
                "Updated the planning system.",
                [],
                "resp_2"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.Is<IReadOnlyList<ConversationToolCall>>(calls =>
                    calls.Count == 1 &&
                    calls[0].Name == AgentToolNames.UpdatePlan),
                session,
                ConversationExecutionPhase.Execution,
                It.Is<IReadOnlySet<string>>(names =>
                    names.Contains(AgentToolNames.UpdatePlan) &&
                    names.Contains(AgentToolNames.FileRead)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "plan_call_1",
                    AgentToolNames.UpdatePlan,
                    ToolResultFactory.Success(
                        "Plan updated: 1 completed, 1 in progress, 1 pending.",
                        new PlanUpdateResult(
                            null,
                            [
                                new PlanUpdateItem("Inspect planning flow", "completed"),
                                new PlanUpdateItem("Wire live plan progress", "in_progress"),
                                new PlanUpdateItem("Run validation", "pending")
                            ],
                            1,
                            1,
                            1),
                        ToolJsonContext.Default.PlanUpdateResult,
                        new ToolRenderPayload("Plan updated", "plan")))
            ]));

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            toolExecutionPipeline.Object,
            toolRegistry.Object,
            configurationAccessor.Object);

        RecordingConversationProgressSink progressSink = new();

        ConversationTurnResult result = await sut.ProcessAsync(
            "Make planning more Codex-like.",
            session,
            progressSink,
            CancellationToken.None);

        result.ResponseText.Should().Be("Updated the planning system.");
        result.ToolExecutionResult.Should().NotBeNull();
        result.ToolExecutionResult!.Results.Should().ContainSingle();
        result.ToolExecutionResult.Results[0].ToolName.Should().Be(AgentToolNames.UpdatePlan);
        progressSink.PlanProgressUpdates.Should().ContainSingle();
        progressSink.PlanProgressUpdates[0].Tasks.Should().Equal(
            "Inspect planning flow",
            "Wire live plan progress",
            "Run validation");
        progressSink.PlanProgressUpdates[0].CompletedTaskCount.Should().Be(1);
        progressSink.PlanProgressUpdates[0].CurrentTaskIndex.Should().Be(1);
        progressSink.CompletedToolBatches.Should().ContainSingle();
        requests.Should().HaveCount(2);
        requests[0].AvailableTools.Select(static tool => tool.Name)
            .Should()
            .Equal(AgentToolNames.UpdatePlan, AgentToolNames.FileRead);
        requests[1].Messages.Should().HaveCount(3);
        requests[1].Messages[2].Role.Should().Be("tool");
    }

    [Fact]
    public async Task ProcessAsync_Should_ThrowConversationPipelineException_When_ApiKeyIsMissing()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            Mock.Of<IConversationProviderClient>(),
            Mock.Of<IConversationResponseMapper>(),
            Mock.Of<IToolExecutionPipeline>(),
            Mock.Of<IToolRegistry>(),
            Mock.Of<IConversationConfigurationAccessor>());

        Func<Task> action = () => sut.ProcessAsync("hello", session, CancellationToken.None);

        await action.Should().ThrowAsync<ConversationPipelineException>()
            .WithMessage("*API key is missing*");
    }

    [Fact]
    public async Task ProcessAsync_Should_PropagateConversationProviderException_When_ProviderFails()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings());

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([CreateToolDefinition(AgentToolNames.ShellCommand)]);

        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConversationProviderException("Provider unavailable."));

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            Mock.Of<IConversationResponseMapper>(),
            Mock.Of<IToolExecutionPipeline>(),
            toolRegistry.Object,
            configurationAccessor.Object);

        Func<Task> action = () => sut.ProcessAsync("hello", session, CancellationToken.None);

        await action.Should().ThrowAsync<ConversationProviderException>()
            .WithMessage("Provider unavailable.");
    }

    [Fact]
    public async Task ProcessAsync_Should_SendPreviousTurns_When_SubsequentTurnRuns()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings());

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    $"resp_{requests.Count}"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .SetupSequence(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse("First reply.", [], "resp_1"))
            .Returns(new ConversationResponse("Second reply.", [], "resp_2"));

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            Mock.Of<IToolExecutionPipeline>(),
            toolRegistry.Object,
            configurationAccessor.Object);

        await sut.ProcessAsync("First question", session, CancellationToken.None);
        await sut.ProcessAsync("What did I just ask?", session, CancellationToken.None);

        requests.Should().HaveCount(2);
        requests[1].Messages.Should().HaveCount(3);
        requests[1].Messages[0].Role.Should().Be("user");
        requests[1].Messages[0].Content.Should().Be("First question");
        requests[1].Messages[1].Role.Should().Be("assistant");
        requests[1].Messages[1].Content.Should().Be("First reply.");
        requests[1].Messages[2].Role.Should().Be("user");
        requests[1].Messages[2].Content.Should().Be("What did I just ask?");
    }

    [Fact]
    public async Task ProcessAsync_Should_TrimStoredHistory_When_MaxHistoryTurnsIsLimited()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings(maxHistoryTurns: 1));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([]);

        List<ConversationProviderRequest> requests = [];
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ConversationProviderPayload(
                    ProviderKind.OpenAiCompatible,
                    """{ "choices": [] }""",
                    $"resp_{requests.Count}"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .SetupSequence(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse("Reply one.", [], "resp_1"))
            .Returns(new ConversationResponse("Reply two.", [], "resp_2"))
            .Returns(new ConversationResponse("Reply three.", [], "resp_3"));

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            Mock.Of<IToolExecutionPipeline>(),
            toolRegistry.Object,
            configurationAccessor.Object);

        await sut.ProcessAsync("Question one", session, CancellationToken.None);
        await sut.ProcessAsync("Question two", session, CancellationToken.None);
        await sut.ProcessAsync("Question three", session, CancellationToken.None);

        requests.Should().HaveCount(3);
        requests[2].Messages.Should().HaveCount(3);
        requests[2].Messages[0].Content.Should().Be("Question two");
        requests[2].Messages[1].Content.Should().Be("Reply two.");
        requests[2].Messages[2].Content.Should().Be("Question three");
        requests[2].Messages.Select(static message => message.Content)
            .Should()
            .NotContain("Question one");
    }

    [Fact]
    public async Task ProcessAsync_Should_ThrowConfiguredLimit_When_ProviderExceedsMaxToolRoundsPerTurn()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings(maxToolRoundsPerTurn: 2));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([CreateToolDefinition(AgentToolNames.FileWrite)]);

        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationProviderPayload(
                ProviderKind.OpenAiCompatible,
                """{ "choices": [] }""",
                "resp_limit"));

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .SetupSequence(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall("call_limit_1", AgentToolNames.FileWrite, """{"path":"index.html"}""")],
                "resp_1"))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall("call_limit_2", AgentToolNames.FileWrite, """{"path":"index.html"}""")],
                "resp_2"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.IsAny<IReadOnlyList<ConversationToolCall>>(),
                session,
                ConversationExecutionPhase.Execution,
                It.Is<IReadOnlySet<string>>(names => names.Contains(AgentToolNames.FileWrite)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "call_limit",
                    AgentToolNames.FileWrite,
                    ToolResultFactory.Success(
                        "Created index.html.",
                        new ToolErrorPayload("ok", "ok"),
                        ToolJsonContext.Default.ToolErrorPayload))
            ]));

        AgentConversationPipeline sut = CreateSut(
            TimeProvider.System,
            new HeuristicTokenEstimator(),
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            toolExecutionPipeline.Object,
            toolRegistry.Object,
            configurationAccessor.Object);

        Func<Task> action = () => sut.ProcessAsync(
            "Keep writing files.",
            session,
            CancellationToken.None);

        await action.Should().ThrowAsync<ConversationResponseException>()
            .WithMessage("*Configured limit: 2 round(s).*");
    }

    private static AgentConversationPipeline CreateSut(
        TimeProvider timeProvider,
        ITokenEstimator tokenEstimator,
        IApiKeySecretStore secretStore,
        IConversationProviderClient providerClient,
        IConversationResponseMapper responseMapper,
        IToolExecutionPipeline toolExecutionPipeline,
        IToolRegistry toolRegistry,
        IConversationConfigurationAccessor configurationAccessor)
    {
        return new AgentConversationPipeline(
            timeProvider,
            tokenEstimator,
            secretStore,
            providerClient,
            responseMapper,
            toolExecutionPipeline,
            toolRegistry,
            configurationAccessor,
            NullLogger<AgentConversationPipeline>.Instance);
    }

    private static ToolDefinition CreateToolDefinition(string name)
    {
        using JsonDocument schemaDocument = JsonDocument.Parse(
            """{ "type": "object", "properties": {}, "additionalProperties": false }""");

        return new ToolDefinition(
            name,
            $"Description for {name}",
            schemaDocument.RootElement.Clone());
    }

    private static ReplSessionContext CreateSession(IAgentProfile? agentProfile = null)
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini", "gpt-4.1"],
            agentProfile);
    }

    private static ConversationSettings CreateSettings(
        string? systemPrompt = null,
        int maxHistoryTurns = 12,
        int maxToolRoundsPerTurn = 32)
    {
        return new ConversationSettings(
            systemPrompt,
            TimeSpan.FromSeconds(30),
            maxHistoryTurns,
            maxToolRoundsPerTurn);
    }

    private sealed class RecordingConversationProgressSink : IConversationProgressSink
    {
        public List<ExecutionPlanProgress> PlanProgressUpdates { get; } = [];

        public List<IReadOnlyList<ConversationToolCall>> StartedToolBatches { get; } = [];

        public List<ToolExecutionBatchResult> CompletedToolBatches { get; } = [];

        public Task ReportExecutionPlanAsync(
            ExecutionPlanProgress executionPlanProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlanProgressUpdates.Add(executionPlanProgress);
            return Task.CompletedTask;
        }

        public Task ReportToolCallsStartedAsync(
            IReadOnlyList<ConversationToolCall> toolCalls,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartedToolBatches.Add(toolCalls);
            return Task.CompletedTask;
        }

        public Task ReportToolResultsAsync(
            ToolExecutionBatchResult toolExecutionResult,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompletedToolBatches.Add(toolExecutionResult);
            return Task.CompletedTask;
        }
    }
}
