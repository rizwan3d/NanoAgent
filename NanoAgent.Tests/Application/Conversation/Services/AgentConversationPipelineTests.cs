using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Conversation.Services;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Services;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace NanoAgent.Tests.Application.Conversation.Services;

public sealed class AgentConversationPipelineTests
{
    [Fact]
    public async Task ProcessAsync_Should_RunPlanningThenExecution_When_ResponseContainsNormalAssistantContent()
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
                CreateToolDefinition("file_read"),
                CreateToolDefinition("file_write"),
                CreateToolDefinition("shell_command")
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
                """
                Objective
                - Plan the refactor first.

                Plan
                1. Inspect the affected files.
                2. Apply the refactor.
                3. Run validation.
                """,
                [],
                "resp_1"))
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

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ResponseText.Should().Be("Implemented the refactor.");
        result.Metrics.Should().NotBeNull();
        requests.Should().HaveCount(2);
        requests[0].SystemPrompt.Should().Contain("Base prompt");
        requests[0].SystemPrompt.Should().Contain("You are NanoAgent in Planning Mode.");
        requests[0].AvailableTools.Select(static tool => tool.Name)
            .Should()
            .Equal("file_read", "shell_command");
        requests[1].SystemPrompt.Should().Contain("Base prompt");
        requests[1].SystemPrompt.Should().Contain("EXECUTION PHASE IS ACTIVE.");
        requests[1].SystemPrompt.Should().Contain("Execution plan for the current request:");
        requests[1].SystemPrompt.Should().Contain("1. Inspect the affected files.");
        requests[1].AvailableTools.Select(static tool => tool.Name)
            .Should()
            .Equal("file_read", "file_write", "shell_command");
        requests[1].Messages.Should().HaveCount(2);
        requests[1].Messages[0].Role.Should().Be("user");
        requests[1].Messages[0].Content.Should().Be("Implement the next refactor.");
        requests[1].Messages[1].Role.Should().Be("assistant");
        requests[1].Messages[1].Content.Should().Contain("Plan");
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[0].Content.Should().Be("Implement the next refactor.");
        session.ConversationHistory[1].Content.Should().Be("Implemented the refactor.");
        session.PendingExecutionPlan.Should().BeNull();
        toolExecutionPipeline.VerifyNoOtherCalls();
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
            .Returns([
                CreateToolDefinition("file_read"),
                CreateToolDefinition("file_write"),
                CreateToolDefinition("shell_command")
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
            .Throws(new ConversationResponseException(
                "The provider returned neither assistant content, a refusal, nor usable tool calls. Finish reason: stop.",
                isRetryableEmptyResponse: true))
            .Returns(new ConversationResponse(
                """
                Objective
                - Plan the refactor first.

                Plan
                1. Inspect the affected files.
                2. Apply the refactor.
                """,
                [],
                "resp_2"))
            .Returns(new ConversationResponse(
                "Implemented the refactor.",
                [],
                "resp_3"));

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
        requests.Should().HaveCount(3);
        requests[0].SystemPrompt.Should().NotContain("previous provider response was empty");
        requests[1].SystemPrompt.Should().Contain("previous provider response was empty");
        requests[1].SystemPrompt.Should().Contain("Base prompt");
        requests[1].SystemPrompt.Should().Contain("You are NanoAgent in Planning Mode.");
        requests[1].Messages.Should().HaveCount(1);
        requests[1].Messages[0].Content.Should().Be("Implement the next refactor.");
        requests[2].SystemPrompt.Should().NotContain("previous provider response was empty");
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[1].Content.Should().Be("Implemented the refactor.");
        toolExecutionPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_Should_ReturnFallbackMessage_When_PlanningOnlyEmptyStopRetryAlsoReturnsEmpty()
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
                CreateToolDefinition("file_read"),
                CreateToolDefinition("file_write"),
                CreateToolDefinition("shell_command")
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
            "Help me plan this refactor.",
            session,
            CancellationToken.None);

        result.ResponseText.Should().Contain("did not receive a usable response");
        result.ResponseText.Should().Contain("provider ended normally");
        requests.Should().HaveCount(2);
        requests[1].SystemPrompt.Should().Contain("previous provider response was empty");
        session.ConversationHistory.Should().BeEmpty();
        session.PendingExecutionPlan.Should().BeNull();
        toolExecutionPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_Should_RunExecution_When_PlanningEmptyStopRetryAlsoReturnsEmpty()
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
                CreateToolDefinition("file_read"),
                CreateToolDefinition("file_write"),
                CreateToolDefinition("shell_command")
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
            .Throws(new ConversationResponseException(
                "The provider returned neither assistant content, a refusal, nor usable tool calls. Finish reason: stop.",
                isRetryableEmptyResponse: true))
            .Throws(new ConversationResponseException(
                "The provider returned neither assistant content, a refusal, nor usable tool calls. Finish reason: stop.",
                isRetryableEmptyResponse: true))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall("exec_call_1", "file_write", """{ "path": "random-01.txt", "content": "42" }""")],
                "resp_3"))
            .Returns(new ConversationResponse(
                "Created ten random-number files.",
                [],
                "resp_4"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.Is<IReadOnlyList<ConversationToolCall>>(calls =>
                    calls.Count == 1 &&
                    calls[0].Name == "file_write"),
                session,
                ConversationExecutionPhase.Execution,
                It.Is<IReadOnlySet<string>>(names => names.Contains("file_write")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "exec_call_1",
                    "file_write",
                    ToolResultFactory.Success(
                        "Created random-01.txt.",
                        new ToolErrorPayload("ok", "ok"),
                        ToolJsonContext.Default.ToolErrorPayload,
                        new ToolRenderPayload("File write complete", "random-01.txt")))]));

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
            "Write random number to 10 different files using write tool.",
            session,
            CancellationToken.None);

        result.ResponseText.Should().Be("Created ten random-number files.");
        result.ToolExecutionResult.Should().NotBeNull();
        result.ToolExecutionResult!.Results.Should().ContainSingle(item => item.ToolName == "file_write");
        requests.Should().HaveCount(4);
        requests[2].SystemPrompt.Should().Contain("EXECUTION PHASE IS ACTIVE.");
        requests[2].SystemPrompt.Should().Contain("minimal plan was synthesized");
        requests[2].SystemPrompt.Should().Contain("Write random number to 10 different files using write tool.");
        requests[2].AvailableTools.Select(static tool => tool.Name).Should().Contain("file_write");
        requests[2].Messages.Should().HaveCount(2);
        requests[2].Messages[0].Content.Should().Be("Write random number to 10 different files using write tool.");
        requests[2].Messages[1].Role.Should().Be("assistant");
        requests[2].Messages[1].Content.Should().Contain("minimal plan was synthesized");
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[1].Content.Should().Be("Created ten random-number files.");
    }

    [Fact]
    public async Task ProcessAsync_Should_SavePlanWithoutExecuting_When_UserRequestsPlanningOnly()
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
                CreateToolDefinition("file_read"),
                CreateToolDefinition("file_write"),
                CreateToolDefinition("shell_command")
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
                """
                Objective
                - Plan the refactor first.

                Plan
                1. Inspect the affected files.
                2. Apply the refactor.
                3. Run validation.
                """,
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

        result.ResponseText.Should().Contain("Plan status");
        result.ResponseText.Should().Contain("saved for the current section");
        requests.Should().HaveCount(1);
        requests[0].SystemPrompt.Should().Contain("You are NanoAgent in Planning Mode.");
        requests[0].AvailableTools.Select(static tool => tool.Name)
            .Should()
            .Equal("file_read", "shell_command");
        session.PendingExecutionPlan.Should().NotBeNull();
        session.PendingExecutionPlan!.SourceUserInput.Should().Be("Help me plan this refactor");
        session.PendingExecutionPlan.Tasks.Should().Equal(
            "Inspect the affected files.",
            "Apply the refactor.",
            "Run validation.");
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[0].Content.Should().Be("Help me plan this refactor");
        session.ConversationHistory[1].Content.Should().Contain("Plan");
        session.ConversationHistory[1].Content.Should().NotContain("Plan status");
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
                CreateToolDefinition("file_read"),
                CreateToolDefinition("file_write"),
                CreateToolDefinition("shell_command")
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
        requests[0].SystemPrompt.Should().Contain("1. Inspect the affected files.");
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
    public async Task ProcessAsync_Should_ExecuteToolCallsAcrossPlanningAndExecutionPhases()
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
                CreateToolDefinition("directory_list"),
                CreateToolDefinition("file_write")
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
                [new ConversationToolCall("plan_call_1", "directory_list", "{}")],
                "resp_1"))
            .Returns(new ConversationResponse(
                """
                Objective
                - Inspect the workspace first.

                Plan
                1. Inspect the workspace.
                2. Update the README.
                3. Verify the result.
                """,
                [],
                "resp_2"))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall("exec_call_1", "file_write", """{ "path": "README.md", "content": "hello" }""")],
                "resp_3"))
            .Returns(new ConversationResponse(
                "Implemented the requested change.",
                [],
                "resp_4"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.Is<IReadOnlyList<ConversationToolCall>>(calls =>
                    calls.Count == 1 &&
                    calls[0].Name == "directory_list"),
                session,
                ConversationExecutionPhase.Planning,
                It.Is<IReadOnlySet<string>>(names =>
                    names.Contains("directory_list") &&
                    !names.Contains("file_write")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "plan_call_1",
                    "directory_list",
                    ToolResultFactory.Success(
                        "Listed directory '.'.",
                        new ToolErrorPayload("ok", "ok"),
                        ToolJsonContext.Default.ToolErrorPayload,
                        new ToolRenderPayload("Directory listing: .", "README.md")))]));
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.Is<IReadOnlyList<ConversationToolCall>>(calls =>
                    calls.Count == 1 &&
                    calls[0].Name == "file_write"),
                session,
                ConversationExecutionPhase.Execution,
                It.Is<IReadOnlySet<string>>(names =>
                    names.Contains("directory_list") &&
                    names.Contains("file_write")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "exec_call_1",
                    "file_write",
                    ToolResultFactory.Success(
                        "Created README.md.",
                        new ToolErrorPayload("ok", "ok"),
                        ToolJsonContext.Default.ToolErrorPayload,
                        new ToolRenderPayload("File write complete", "README.md")))]));

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
            .Equal("directory_list", "file_write");
        progressSink.PlanProgressUpdates.Select(static update => update.CompletedTaskCount)
            .Should()
            .Equal(0, 1, 3);
        progressSink.PlanProgressUpdates[0].Tasks.Should().Equal(
            "Inspect the workspace.",
            "Update the README.",
            "Verify the result.");
        progressSink.StartedToolBatches.Should().HaveCount(2);
        progressSink.CompletedToolBatches.Should().HaveCount(2);
        requests.Should().HaveCount(4);
        requests[1].Messages.Should().HaveCount(3);
        requests[1].Messages[1].Role.Should().Be("assistant");
        requests[1].Messages[1].ToolCalls.Should().ContainSingle();
        requests[1].Messages[1].ToolCalls[0].Name.Should().Be("directory_list");
        requests[1].Messages[2].Role.Should().Be("tool");

        using JsonDocument toolFeedbackDocument = JsonDocument.Parse(requests[3].Messages[^1].Content!);
        JsonElement toolFeedback = toolFeedbackDocument.RootElement;
        toolFeedback.GetProperty("ToolName").GetString().Should().Be("file_write");
        toolFeedback.GetProperty("Status").GetString().Should().Be("Success");
        toolFeedback.GetProperty("IsSuccess").GetBoolean().Should().BeTrue();
        toolFeedback.GetProperty("Message").GetString().Should().Be("Created README.md.");
        toolFeedback.GetProperty("Render").GetProperty("Title").GetString().Should().Be("File write complete");
        toolFeedback.GetProperty("Data").GetProperty("Code").GetString().Should().Be("ok");
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[1].Content.Should().Be("Implemented the requested change.");
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
            .Returns([CreateToolDefinition("shell_command")]);

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
            .Returns(new ConversationResponse("First plan.", [], "resp_1"))
            .Returns(new ConversationResponse("First reply.", [], "resp_2"))
            .Returns(new ConversationResponse("Second plan.", [], "resp_3"))
            .Returns(new ConversationResponse("Second reply.", [], "resp_4"));

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

        requests.Should().HaveCount(4);
        requests[2].Messages.Should().HaveCount(3);
        requests[2].Messages[0].Role.Should().Be("user");
        requests[2].Messages[0].Content.Should().Be("First question");
        requests[2].Messages[1].Role.Should().Be("assistant");
        requests[2].Messages[1].Content.Should().Be("First reply.");
        requests[2].Messages[2].Role.Should().Be("user");
        requests[2].Messages[2].Content.Should().Be("What did I just ask?");
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
            .Returns(new ConversationResponse("Plan one.", [], "resp_1"))
            .Returns(new ConversationResponse("Reply one.", [], "resp_2"))
            .Returns(new ConversationResponse("Plan two.", [], "resp_3"))
            .Returns(new ConversationResponse("Reply two.", [], "resp_4"))
            .Returns(new ConversationResponse("Plan three.", [], "resp_5"))
            .Returns(new ConversationResponse("Reply three.", [], "resp_6"));

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

        requests.Should().HaveCount(6);
        requests[4].Messages.Should().HaveCount(3);
        requests[4].Messages[0].Content.Should().Be("Question two");
        requests[4].Messages[1].Content.Should().Be("Reply two.");
        requests[4].Messages[2].Content.Should().Be("Question three");
        requests[4].Messages.Select(static message => message.Content)
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
            .Returns([CreateToolDefinition("file_write")]);

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
                "Plan first.",
                [],
                "resp_1"))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall("call_limit_1", "file_write", """{"path":"index.html"}""")],
                "resp_2"))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall("call_limit_2", "file_write", """{"path":"index.html"}""")],
                "resp_3"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.IsAny<IReadOnlyList<ConversationToolCall>>(),
                session,
                ConversationExecutionPhase.Execution,
                It.Is<IReadOnlySet<string>>(names => names.Contains("file_write")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "call_limit",
                    "file_write",
                    ToolResultFactory.Success(
                        "Created index.html.",
                        new ToolErrorPayload("ok", "ok"),
                        ToolJsonContext.Default.ToolErrorPayload))]));

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

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini", "gpt-4.1"]);
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
