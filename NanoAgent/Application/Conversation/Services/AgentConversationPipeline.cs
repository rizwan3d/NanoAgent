using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Conversation.Serialization;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Logging;
using NanoAgent.Application.Models;
using NanoAgent.Application.Planning;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace NanoAgent.Application.Conversation.Services;

internal sealed class AgentConversationPipeline : IConversationPipeline
{
    private readonly TimeProvider _timeProvider;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IConversationProviderClient _providerClient;
    private readonly IConversationResponseMapper _responseMapper;
    private readonly IToolExecutionPipeline _toolExecutionPipeline;
    private readonly IToolRegistry _toolRegistry;
    private readonly IConversationConfigurationAccessor _configurationAccessor;
    private readonly ILogger<AgentConversationPipeline> _logger;

    public AgentConversationPipeline(
        TimeProvider timeProvider,
        ITokenEstimator tokenEstimator,
        IApiKeySecretStore secretStore,
        IConversationProviderClient providerClient,
        IConversationResponseMapper responseMapper,
        IToolExecutionPipeline toolExecutionPipeline,
        IToolRegistry toolRegistry,
        IConversationConfigurationAccessor configurationAccessor,
        ILogger<AgentConversationPipeline> logger)
    {
        _timeProvider = timeProvider;
        _tokenEstimator = tokenEstimator;
        _secretStore = secretStore;
        _providerClient = providerClient;
        _responseMapper = responseMapper;
        _toolExecutionPipeline = toolExecutionPipeline;
        _toolRegistry = toolRegistry;
        _configurationAccessor = configurationAccessor;
        _logger = logger;
    }

    public async Task<ConversationTurnResult> ProcessAsync(
        string input,
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        return await ProcessAsync(
            input,
            session,
            NoOpConversationProgressSink.Instance,
            cancellationToken);
    }

    public async Task<ConversationTurnResult> ProcessAsync(
        string input,
        ReplSessionContext session,
        IConversationProgressSink progressSink,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(progressSink);
        cancellationToken.ThrowIfCancellationRequested();

        string apiKey = await _secretStore.LoadAsync(cancellationToken)
            ?? throw new ConversationPipelineException(
                "Conversation cannot start because the API key is missing.");

        ConversationSettings settings = _configurationAccessor.GetSettings();
        IReadOnlyList<ToolDefinition> allToolDefinitions = _toolRegistry.GetToolDefinitions();
        IReadOnlyList<ToolDefinition> planningToolDefinitions = PlanningModePolicy.FilterPlanningTools(
            allToolDefinitions);
        IReadOnlySet<string> planningToolNames = planningToolDefinitions
            .Select(static definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<string> executionToolNames = allToolDefinitions
            .Select(static definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(settings.RequestTimeout);
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        string normalizedInput = input.Trim();
        List<ConversationRequestMessage> planningMessages =
        [
            .. session.GetConversationHistory(settings.MaxHistoryTurns),
            ConversationRequestMessage.User(normalizedInput)
        ];

        ApplicationLogMessages.ConversationRequestStarted(
            _logger,
            session.ProviderName,
            session.ActiveModelId);

        PhaseExecutionResult planningResult = await RunPhaseAsync(
            apiKey,
            session,
            planningMessages,
            PlanningModePolicy.CreatePlanningSystemPrompt(settings.SystemPrompt),
            planningToolDefinitions,
            planningToolNames,
            ConversationExecutionPhase.Planning,
            progressSink,
            settings,
            timeoutSource,
            executionPlanTracker: null,
            cancellationToken);

        ExecutionPlanTracker? executionPlanTracker = ExecutionPlanTracker.Create(
            PlanningModePolicy.ExtractPlanTasks(planningResult.AssistantMessage));
        if (executionPlanTracker is not null)
        {
            await progressSink.ReportExecutionPlanAsync(
                executionPlanTracker.CreateSnapshot(),
                cancellationToken);
        }

        List<ConversationRequestMessage> executionMessages =
        [
            .. planningResult.Messages,
            ConversationRequestMessage.AssistantMessage(planningResult.AssistantMessage)
        ];

        PhaseExecutionResult executionResult = await RunPhaseAsync(
            apiKey,
            session,
            executionMessages,
            PlanningModePolicy.CreateExecutionSystemPrompt(
                settings.SystemPrompt,
                planningResult.AssistantMessage),
            allToolDefinitions,
            executionToolNames,
            ConversationExecutionPhase.Execution,
            progressSink,
            settings,
            timeoutSource,
            executionPlanTracker,
            cancellationToken);

        ApplicationLogMessages.ConversationAssistantMessageReceived(_logger);
        session.AddConversationTurn(normalizedInput, executionResult.AssistantMessage);

        int? completionTokens = null;
        if (planningResult.HasReportedCompletionTokens || executionResult.HasReportedCompletionTokens)
        {
            completionTokens = planningResult.TotalCompletionTokens + executionResult.TotalCompletionTokens;
        }

        ToolExecutionBatchResult? toolExecutionResult = CombineToolExecutionResults(
            planningResult.ExecutedToolResults,
            executionResult.ExecutedToolResults);

        return ConversationTurnResult.AssistantMessage(
            executionResult.AssistantMessage,
            toolExecutionResult,
            CreateMetrics(
                startedAt,
                executionResult.AssistantMessage,
                completionTokens));
    }

    private ConversationTurnMetrics CreateMetrics(
        DateTimeOffset startedAt,
        string responseText,
        int? completionTokens)
    {
        TimeSpan elapsed = _timeProvider.GetUtcNow() - startedAt;
        int estimatedOutputTokens = completionTokens is > 0
            ? completionTokens.Value
            : _tokenEstimator.Estimate(responseText);
        return new ConversationTurnMetrics(elapsed, estimatedOutputTokens);
    }

    private static string CreateToolFeedbackContent(ToolInvocationResult invocationResult)
    {
        ArgumentNullException.ThrowIfNull(invocationResult);

        using JsonDocument dataDocument = JsonDocument.Parse(invocationResult.Result.JsonResult);

        ToolFeedbackRenderPayload? render = invocationResult.Result.RenderPayload is null
            ? null
            : new ToolFeedbackRenderPayload(
                invocationResult.Result.RenderPayload.Title,
                invocationResult.Result.RenderPayload.Text);

        ToolFeedbackPayload payload = new(
            invocationResult.ToolName,
            invocationResult.Result.Status,
            invocationResult.Result.IsSuccess,
            invocationResult.Result.Message,
            dataDocument.RootElement.Clone(),
            render);

        return JsonSerializer.Serialize(
            payload,
            ConversationJsonContext.Default.ToolFeedbackPayload);
    }

    private async Task<PhaseExecutionResult> RunPhaseAsync(
        string apiKey,
        ReplSessionContext session,
        IReadOnlyList<ConversationRequestMessage> initialMessages,
        string? systemPrompt,
        IReadOnlyList<ToolDefinition> availableTools,
        IReadOnlySet<string> allowedToolNames,
        ConversationExecutionPhase executionPhase,
        IConversationProgressSink progressSink,
        ConversationSettings settings,
        CancellationTokenSource timeoutSource,
        ExecutionPlanTracker? executionPlanTracker,
        CancellationToken cancellationToken)
    {
        List<ConversationRequestMessage> messages = initialMessages.ToList();
        List<ToolInvocationResult> executedToolResults = [];
        int totalCompletionTokens = 0;
        bool hasReportedCompletionTokens = false;

        for (int round = 0; round < settings.MaxToolRoundsPerTurn; round++)
        {
            ConversationProviderPayload providerPayload;

            try
            {
                providerPayload = await _providerClient.SendAsync(
                    new ConversationProviderRequest(
                        session.ProviderProfile,
                        apiKey,
                        session.ActiveModelId,
                        messages,
                        systemPrompt,
                        availableTools),
                    timeoutSource.Token);
            }
            catch (ConversationProviderException)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
            {
                throw new ConversationProviderException(
                    $"The conversation request timed out after {settings.RequestTimeout.TotalSeconds:0} seconds.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ConversationProviderException(
                    "The configured provider failed while processing the conversation request.",
                    exception);
            }

            ConversationResponse response;

            try
            {
                response = _responseMapper.Map(providerPayload);
            }
            catch (ConversationResponseException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ConversationResponseException(
                    "The provider response could not be normalized into the internal conversation model.",
                    exception);
            }

            if (response.CompletionTokens is > 0)
            {
                totalCompletionTokens += response.CompletionTokens.Value;
                hasReportedCompletionTokens = true;
            }

            if (response.HasToolCalls)
            {
                ApplicationLogMessages.ConversationToolHandoffStarted(
                    _logger,
                    response.ToolCalls.Count);

                await progressSink.ReportToolCallsStartedAsync(
                    response.ToolCalls,
                    cancellationToken);

                ToolExecutionBatchResult toolExecutionResult = await _toolExecutionPipeline.ExecuteAsync(
                    response.ToolCalls,
                    session,
                    executionPhase,
                    allowedToolNames,
                    cancellationToken);

                ApplicationLogMessages.ConversationToolHandoffCompleted(_logger);
                executedToolResults.AddRange(toolExecutionResult.Results);

                await progressSink.ReportToolResultsAsync(
                    toolExecutionResult,
                    cancellationToken);

                if (executionPhase == ConversationExecutionPhase.Execution &&
                    executionPlanTracker is not null)
                {
                    await progressSink.ReportExecutionPlanAsync(
                        executionPlanTracker.Advance(),
                        cancellationToken);
                }

                messages.Add(ConversationRequestMessage.AssistantToolCalls(
                    response.ToolCalls,
                    response.AssistantMessage));

                foreach (ToolInvocationResult invocationResult in toolExecutionResult.Results)
                {
                    messages.Add(ConversationRequestMessage.ToolResult(
                        invocationResult.ToolCallId,
                        CreateToolFeedbackContent(invocationResult)));
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(response.AssistantMessage))
            {
                throw new ConversationResponseException(
                    "The provider response did not contain an assistant message or any tool calls.");
            }

            return new PhaseExecutionResult(
                response.AssistantMessage,
                messages,
                executedToolResults,
                totalCompletionTokens,
                hasReportedCompletionTokens);
        }

        throw new ConversationResponseException(
            $"The provider requested too many sequential tool rounds without producing a final assistant message. " +
            $"Configured limit: {settings.MaxToolRoundsPerTurn} round(s).");
    }

    private static ToolExecutionBatchResult? CombineToolExecutionResults(
        IReadOnlyList<ToolInvocationResult> planningResults,
        IReadOnlyList<ToolInvocationResult> executionResults)
    {
        List<ToolInvocationResult> results = [];
        if (planningResults.Count > 0)
        {
            results.AddRange(planningResults);
        }

        if (executionResults.Count > 0)
        {
            results.AddRange(executionResults);
        }

        return results.Count == 0
            ? null
            : new ToolExecutionBatchResult(results);
    }

    private sealed class NoOpConversationProgressSink : IConversationProgressSink
    {
        public static NoOpConversationProgressSink Instance { get; } = new();

        public Task ReportToolCallsStartedAsync(
            IReadOnlyList<ConversationToolCall> toolCalls,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ReportExecutionPlanAsync(
            ExecutionPlanProgress executionPlanProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(executionPlanProgress);
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

    private sealed class ExecutionPlanTracker
    {
        private readonly IReadOnlyList<string> _tasks;
        private int _completedTaskCount;

        private ExecutionPlanTracker(IReadOnlyList<string> tasks)
        {
            _tasks = tasks;
        }

        public static ExecutionPlanTracker? Create(IReadOnlyList<string> tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);

            return tasks.Count == 0
                ? null
                : new ExecutionPlanTracker(tasks.ToArray());
        }

        public ExecutionPlanProgress Advance()
        {
            if (_completedTaskCount < _tasks.Count)
            {
                _completedTaskCount++;
            }

            return CreateSnapshot();
        }

        public ExecutionPlanProgress CreateSnapshot()
        {
            return new ExecutionPlanProgress(
                _tasks,
                _completedTaskCount);
        }
    }

    private sealed class PhaseExecutionResult
    {
        public PhaseExecutionResult(
            string assistantMessage,
            IReadOnlyList<ConversationRequestMessage> messages,
            IReadOnlyList<ToolInvocationResult> executedToolResults,
            int totalCompletionTokens,
            bool hasReportedCompletionTokens)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assistantMessage);
            ArgumentNullException.ThrowIfNull(messages);
            ArgumentNullException.ThrowIfNull(executedToolResults);

            AssistantMessage = assistantMessage.Trim();
            Messages = messages.ToArray();
            ExecutedToolResults = executedToolResults.ToArray();
            TotalCompletionTokens = totalCompletionTokens;
            HasReportedCompletionTokens = hasReportedCompletionTokens;
        }

        public string AssistantMessage { get; }

        public IReadOnlyList<ToolInvocationResult> ExecutedToolResults { get; }

        public bool HasReportedCompletionTokens { get; }

        public IReadOnlyList<ConversationRequestMessage> Messages { get; }

        public int TotalCompletionTokens { get; }
    }
}
