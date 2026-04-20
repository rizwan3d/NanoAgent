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
    public async Task ProcessAsync_Should_ReturnAssistantMessage_When_ResponseContainsNormalAssistantContent()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings("You are helpful."));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([
                CreateToolDefinition("file_read")
            ]);

        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.Is<ConversationProviderRequest>(request =>
                    request.AvailableTools.Count == 1 &&
                    request.AvailableTools[0].Name == "file_read"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationProviderPayload(
                ProviderKind.OpenAiCompatible,
                """{ "choices": [] }""",
                "resp_123"));

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .Setup(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse("Ready to help.", [], "resp_123"));

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
            "Plan the next refactor.",
            session,
            CancellationToken.None);

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ResponseText.Should().Be("Ready to help.");
        result.Metrics.Should().NotBeNull();
        result.Metrics!.EstimatedOutputTokens.Should().BeGreaterThan(0);
        session.ConversationHistory.Should().HaveCount(2);
        toolExecutionPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_Should_ContinueConversationAfterToolExecution_When_ResponseContainsToolCalls()
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
                CreateToolDefinition("directory_list")
            ]);

        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        ConversationProviderRequest? followUpRequest = null;
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                if (followUpRequest is null)
                {
                    followUpRequest = request.Messages.Count > 1
                        ? request
                        : null;
                }

                return Task.FromResult(request.Messages.Count == 1
                    ? new ConversationProviderPayload(
                        ProviderKind.OpenAiCompatible,
                        """{ "choices": [] }""",
                        "resp_456")
                    : new ConversationProviderPayload(
                        ProviderKind.OpenAiCompatible,
                        """{ "choices": [] }""",
                        "resp_789"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .SetupSequence(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall("call_1", "directory_list", "{}")],
                "resp_456"))
            .Returns(new ConversationResponse(
                "I inspected the workspace and can create the requested files next.",
                [],
                "resp_789"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.Is<IReadOnlyList<ConversationToolCall>>(calls => calls.Count == 1 && calls[0].Name == "directory_list"),
                session,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "call_1",
                    "directory_list",
                    ToolResultFactory.Success(
                        "Listed directory '.'.",
                        new ToolErrorPayload("ok", "ok"),
                        ToolJsonContext.Default.ToolErrorPayload,
                        new ToolRenderPayload("Directory listing: .", "file: README.md")))
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
            "Which models can I use?",
            session,
            progressSink,
            CancellationToken.None);

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ResponseText.Should().Be("I inspected the workspace and can create the requested files next.");
        result.ToolExecutionResult.Should().NotBeNull();
        result.ToolExecutionResult!.Results.Should().ContainSingle();
        result.ToolExecutionResult.Results[0].ToolName.Should().Be("directory_list");
        result.Metrics.Should().NotBeNull();
        followUpRequest.Should().NotBeNull();
        followUpRequest!.Messages.Should().HaveCount(3);
        followUpRequest.Messages[0].Role.Should().Be("user");
        followUpRequest.Messages[1].Role.Should().Be("assistant");
        followUpRequest.Messages[1].ToolCalls.Should().ContainSingle();
        followUpRequest.Messages[1].ToolCalls[0].Name.Should().Be("directory_list");
        followUpRequest.Messages[2].Role.Should().Be("tool");
        followUpRequest.Messages[2].ToolCallId.Should().Be("call_1");

        using JsonDocument toolFeedbackDocument = JsonDocument.Parse(followUpRequest.Messages[2].Content!);
        JsonElement toolFeedback = toolFeedbackDocument.RootElement;
        toolFeedback.GetProperty("ToolName").GetString().Should().Be("directory_list");
        toolFeedback.GetProperty("Status").GetString().Should().Be("Success");
        toolFeedback.GetProperty("IsSuccess").GetBoolean().Should().BeTrue();
        toolFeedback.GetProperty("Message").GetString().Should().Be("Listed directory '.'.");
        toolFeedback.GetProperty("Render").GetProperty("Title").GetString().Should().Be("Directory listing: .");
        toolFeedback.GetProperty("Data").GetProperty("Code").GetString().Should().Be("ok");
        toolFeedback.GetProperty("Data").GetProperty("Message").GetString().Should().Be("ok");
        progressSink.StartedToolBatches.Should().ContainSingle();
        progressSink.StartedToolBatches[0].Should().ContainSingle(tool => tool.Name == "directory_list");
        progressSink.CompletedToolBatches.Should().ContainSingle();
        progressSink.CompletedToolBatches[0].Results.Should().ContainSingle(result => result.ToolName == "directory_list");
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[1].Content.Should().Be("I inspected the workspace and can create the requested files next.");
    }

    [Fact]
    public async Task ProcessAsync_Should_SendStructuredFailureFeedback_When_ToolExecutionFails()
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
                CreateToolDefinition("file_write")
            ]);

        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        ConversationProviderRequest? followUpRequest = null;
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<ConversationProviderRequest, CancellationToken>((request, _) =>
            {
                if (request.Messages.Count > 1)
                {
                    followUpRequest = request;
                }

                return Task.FromResult(request.Messages.Count == 1
                    ? new ConversationProviderPayload(
                        ProviderKind.OpenAiCompatible,
                        """{ "choices": [] }""",
                        "resp_fail_1")
                    : new ConversationProviderPayload(
                        ProviderKind.OpenAiCompatible,
                        """{ "choices": [] }""",
                        "resp_fail_2"));
            });

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .SetupSequence(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall("call_fail_1", "file_write", """{"path":"index.html"}""")],
                "resp_fail_1"))
            .Returns(new ConversationResponse(
                "The write failed because the tool arguments were incomplete.",
                [],
                "resp_fail_2"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.Is<IReadOnlyList<ConversationToolCall>>(calls => calls.Count == 1 && calls[0].Name == "file_write"),
                session,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "call_fail_1",
                    "file_write",
                    ToolResultFactory.InvalidArguments(
                        "missing_content",
                        "The 'content' argument is required.",
                        new ToolRenderPayload("File write rejected", "The tool needs both path and content.")))
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

        ConversationTurnResult result = await sut.ProcessAsync(
            "Write the homepage file.",
            session,
            CancellationToken.None);

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ResponseText.Should().Be("The write failed because the tool arguments were incomplete.");
        followUpRequest.Should().NotBeNull();
        result.ToolExecutionResult.Should().NotBeNull();
        result.ToolExecutionResult!.Results.Should().ContainSingle();
        result.ToolExecutionResult.Results[0].ToolName.Should().Be("file_write");

        using JsonDocument toolFeedbackDocument = JsonDocument.Parse(followUpRequest!.Messages[2].Content!);
        JsonElement toolFeedback = toolFeedbackDocument.RootElement;
        toolFeedback.GetProperty("ToolName").GetString().Should().Be("file_write");
        toolFeedback.GetProperty("Status").GetString().Should().Be("InvalidArguments");
        toolFeedback.GetProperty("IsSuccess").GetBoolean().Should().BeFalse();
        toolFeedback.GetProperty("Message").GetString().Should().Be("The 'content' argument is required.");
        toolFeedback.GetProperty("Data").GetProperty("Code").GetString().Should().Be("missing_content");
        toolFeedback.GetProperty("Render").GetProperty("Title").GetString().Should().Be("File write rejected");
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
            .Returns([
                CreateToolDefinition("shell_command")
            ]);

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
    public async Task ProcessAsync_Should_AllowLongerSequentialToolChains_When_MaxToolRoundsPerTurnIsRaised()
    {
        ReplSessionContext session = CreateSession();
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationConfigurationAccessor> configurationAccessor = new(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetSettings())
            .Returns(CreateSettings(maxToolRoundsPerTurn: 16));

        Mock<IToolRegistry> toolRegistry = new(MockBehavior.Strict);
        toolRegistry
            .Setup(registry => registry.GetToolDefinitions())
            .Returns([
                CreateToolDefinition("file_write")
            ]);

        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.IsAny<ConversationProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationProviderPayload(
                ProviderKind.OpenAiCompatible,
                """{ "choices": [] }""",
                "resp_tool_chain"));

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        MockSequence responseSequence = new();
        for (int index = 1; index <= 10; index++)
        {
            int fileIndex = index;
            responseMapper
                .InSequence(responseSequence)
                .Setup(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
                .Returns(new ConversationResponse(
                    null,
                    [new ConversationToolCall($"call_{fileIndex}", "file_write", $$"""{"path":"random_number_{{fileIndex}}.txt","content":"Random Number for File {{fileIndex}}"}""")],
                    $"resp_tool_chain_{fileIndex}"));
        }

        responseMapper
            .InSequence(responseSequence)
            .Setup(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                "Created all requested files.",
                [],
                "resp_tool_chain_final"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.IsAny<IReadOnlyList<ConversationToolCall>>(),
                session,
                It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyList<ConversationToolCall>, ReplSessionContext, CancellationToken>((calls, _, _) =>
                Task.FromResult(new ToolExecutionBatchResult([
                    new ToolInvocationResult(
                        calls[0].Id,
                        "file_write",
                        ToolResultFactory.Success(
                            $"Created {calls[0].Name}.",
                            new ToolErrorPayload("ok", "ok"),
                            ToolJsonContext.Default.ToolErrorPayload))
                ])));

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
            "Write 10 files with random numbers.",
            session,
            CancellationToken.None);

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ResponseText.Should().Be("Created all requested files.");
        result.ToolExecutionResult.Should().NotBeNull();
        result.ToolExecutionResult!.Results.Should().HaveCount(10);
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
            .Returns([
                CreateToolDefinition("file_write")
            ]);

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
            .Setup(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                null,
                [new ConversationToolCall("call_limit", "file_write", """{"path":"index.html"}""")],
                "resp_limit"));

        Mock<IToolExecutionPipeline> toolExecutionPipeline = new(MockBehavior.Strict);
        toolExecutionPipeline
            .Setup(pipeline => pipeline.ExecuteAsync(
                It.IsAny<IReadOnlyList<ConversationToolCall>>(),
                session,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionBatchResult([
                new ToolInvocationResult(
                    "call_limit",
                    "file_write",
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
        public List<IReadOnlyList<ConversationToolCall>> StartedToolBatches { get; } = [];

        public List<ToolExecutionBatchResult> CompletedToolBatches { get; } = [];

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
