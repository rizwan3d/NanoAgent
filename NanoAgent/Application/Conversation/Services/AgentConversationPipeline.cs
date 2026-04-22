using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Conversation.Serialization;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Logging;
using NanoAgent.Application.Models;
using NanoAgent.Application.Planning;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace NanoAgent.Application.Conversation.Services;

internal sealed class AgentConversationPipeline : IConversationPipeline
{
    private const int EmptyResponseRetryLimit = 1;
    private const string EmptyResponseFallbackMessage =
        "I did not receive a usable response from the provider after retrying. " +
        "The provider ended normally but returned no assistant content or tool calls. Please try again.";
    private const string EmptyResponseRetryInstruction =
        "The previous provider response was empty even though it ended normally. " +
        "Return a non-empty assistant message or call an available tool.";

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
        string? profileSystemPrompt = CreateProfileSystemPrompt(settings.SystemPrompt, session);
        IReadOnlyList<ToolDefinition> availableToolDefinitions = GetProfileToolDefinitions(session);
        IReadOnlySet<string> availableToolNames = availableToolDefinitions
            .Select(static definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(settings.RequestTimeout);
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        string normalizedInput = input.Trim();

        if (session.PendingExecutionPlan is not null &&
            PlanningModePolicy.IsExecutionApproval(normalizedInput))
        {
            return await ExecuteApprovedPlanAsync(
                normalizedInput,
                session,
                progressSink,
                settings,
                apiKey,
                availableToolDefinitions,
                availableToolNames,
                timeoutSource,
                startedAt,
                cancellationToken);
        }

        List<ConversationRequestMessage> messages =
        [
            .. session.GetConversationHistory(settings.MaxHistoryTurns),
            ConversationRequestMessage.User(normalizedInput)
        ];

        ApplicationLogMessages.ConversationRequestStarted(
            _logger,
            session.ProviderName,
            session.ActiveModelId);

        PhaseExecutionResult result = await RunPhaseAsync(
            apiKey,
            session,
            messages,
            PlanningModePolicy.CreateToolDrivenConversationSystemPrompt(profileSystemPrompt),
            availableToolDefinitions,
            availableToolNames,
            ConversationExecutionPhase.Execution,
            progressSink,
            settings,
            timeoutSource,
            executionPlanTracker: null,
            cancellationToken);

        if (result.IsProviderEmptyResponseFallback)
        {
            return CreatePhaseResultTurnResult(
                startedAt,
                result,
                CreateBatchResult(result.ExecutedToolResults),
                GetCompletionTokens(result));
        }

        ApplicationLogMessages.ConversationAssistantMessageReceived(_logger);
        session.ClearPendingExecutionPlan();
        session.AddConversationTurn(normalizedInput, result.AssistantMessage);

        return ConversationTurnResult.AssistantMessage(
            result.AssistantMessage,
            CreateBatchResult(result.ExecutedToolResults),
            CreateMetrics(
                startedAt,
                result.AssistantMessage,
                GetCompletionTokens(result)));
    }

    private async Task<ConversationTurnResult> ExecuteApprovedPlanAsync(
        string normalizedInput,
        ReplSessionContext session,
        IConversationProgressSink progressSink,
        ConversationSettings settings,
        string apiKey,
        IReadOnlyList<ToolDefinition> allToolDefinitions,
        IReadOnlySet<string> executionToolNames,
        CancellationTokenSource timeoutSource,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        PendingExecutionPlan pendingPlan = session.PendingExecutionPlan
            ?? throw new InvalidOperationException("A pending execution plan is required.");

        ExecutionPlanTracker? executionPlanTracker = ExecutionPlanTracker.Create(
            pendingPlan.Tasks);
        if (executionPlanTracker is not null)
        {
            await progressSink.ReportExecutionPlanAsync(
                executionPlanTracker.CreateSnapshot(),
                cancellationToken);
        }

        List<ConversationRequestMessage> executionMessages =
        [
            .. session.GetConversationHistory(settings.MaxHistoryTurns),
            ConversationRequestMessage.User(normalizedInput)
        ];

        PhaseExecutionResult executionResult = await RunPhaseAsync(
            apiKey,
            session,
            executionMessages,
            PlanningModePolicy.CreateExecutionSystemPrompt(
                CreateProfileSystemPrompt(settings.SystemPrompt, session),
                pendingPlan.PlanningSummary),
            allToolDefinitions,
            executionToolNames,
            ConversationExecutionPhase.Execution,
            progressSink,
            settings,
            timeoutSource,
            executionPlanTracker,
            cancellationToken);

        if (executionResult.IsProviderEmptyResponseFallback)
        {
            return CreatePhaseResultTurnResult(
                startedAt,
                executionResult,
                CreateBatchResult(executionResult.ExecutedToolResults),
                GetCompletionTokens(executionResult));
        }

        if (executionPlanTracker is not null)
        {
            await progressSink.ReportExecutionPlanAsync(
                executionPlanTracker.Complete(),
                cancellationToken);
        }

        ApplicationLogMessages.ConversationAssistantMessageReceived(_logger);
        session.ClearPendingExecutionPlan();
        session.AddConversationTurn(normalizedInput, executionResult.AssistantMessage);

        return ConversationTurnResult.AssistantMessage(
            executionResult.AssistantMessage,
            CreateBatchResult(executionResult.ExecutedToolResults),
            CreateMetrics(
                startedAt,
                executionResult.AssistantMessage,
                GetCompletionTokens(executionResult)));
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

    private ConversationTurnResult CreatePhaseResultTurnResult(
        DateTimeOffset startedAt,
        PhaseExecutionResult phaseResult,
        ToolExecutionBatchResult? toolExecutionResult,
        int? completionTokens)
    {
        return ConversationTurnResult.AssistantMessage(
            phaseResult.AssistantMessage,
            toolExecutionResult,
            CreateMetrics(
                startedAt,
                phaseResult.AssistantMessage,
                completionTokens));
    }

    private static string CreateEmptyResponseRetrySystemPrompt(string? systemPrompt)
    {
        return string.IsNullOrWhiteSpace(systemPrompt)
            ? EmptyResponseRetryInstruction
            : $"{systemPrompt.Trim()}{Environment.NewLine}{Environment.NewLine}{EmptyResponseRetryInstruction}";
    }

    private IReadOnlyList<ToolDefinition> GetProfileToolDefinitions(ReplSessionContext session)
    {
        IReadOnlySet<string> enabledTools = session.AgentProfile.EnabledTools;

        return _toolRegistry.GetToolDefinitions()
            .Where(definition => enabledTools.Contains(definition.Name))
            .ToArray();
    }

    private static string? CreateProfileSystemPrompt(
        string? basePrompt,
        ReplSessionContext session)
    {
        string? contribution = session.AgentProfile.SystemPromptContribution;
        if (string.IsNullOrWhiteSpace(contribution))
        {
            return basePrompt;
        }

        if (string.IsNullOrWhiteSpace(basePrompt))
        {
            return contribution.Trim();
        }

        return $"{basePrompt.Trim()}{Environment.NewLine}{Environment.NewLine}{contribution.Trim()}";
    }

    private static int? GetCompletionTokens(PhaseExecutionResult phaseResult)
    {
        return phaseResult.HasReportedCompletionTokens
            ? phaseResult.TotalCompletionTokens
            : null;
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
            ConversationResponse? response = await SendAndMapResponseAsync(
                apiKey,
                session,
                messages,
                systemPrompt,
                availableTools,
                settings,
                timeoutSource,
                cancellationToken);

            if (response is null)
            {
                return CreateEmptyResponseFallback(
                    executedToolResults,
                    totalCompletionTokens,
                    hasReportedCompletionTokens);
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

                bool reportedPlanUpdate = await ReportPlanUpdatesAsync(
                    toolExecutionResult,
                    progressSink,
                    cancellationToken);

                await progressSink.ReportToolResultsAsync(
                    toolExecutionResult,
                    cancellationToken);

                if (executionPhase == ConversationExecutionPhase.Execution &&
                    executionPlanTracker is not null &&
                    !reportedPlanUpdate)
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
                executedToolResults,
                totalCompletionTokens,
                hasReportedCompletionTokens);
        }

        throw new ConversationResponseException(
            $"The provider requested too many sequential tool rounds without producing a final assistant message. " +
            $"Configured limit: {settings.MaxToolRoundsPerTurn} round(s).");
    }

    private static async Task<bool> ReportPlanUpdatesAsync(
        ToolExecutionBatchResult toolExecutionResult,
        IConversationProgressSink progressSink,
        CancellationToken cancellationToken)
    {
        bool reportedPlanUpdate = false;

        foreach (ToolInvocationResult invocationResult in toolExecutionResult.Results)
        {
            if (!TryCreatePlanProgress(invocationResult, out ExecutionPlanProgress? progress) ||
                progress is null)
            {
                continue;
            }

            await progressSink.ReportExecutionPlanAsync(
                progress,
                cancellationToken);
            reportedPlanUpdate = true;
        }

        return reportedPlanUpdate;
    }

    private static bool TryCreatePlanProgress(
        ToolInvocationResult invocationResult,
        out ExecutionPlanProgress? progress)
    {
        progress = null;

        if (!invocationResult.Result.IsSuccess ||
            !string.Equals(invocationResult.ToolName, AgentToolNames.UpdatePlan, StringComparison.Ordinal))
        {
            return false;
        }

        PlanUpdateResult? result;
        try
        {
            result = JsonSerializer.Deserialize(
                invocationResult.Result.JsonResult,
                ToolJsonContext.Default.PlanUpdateResult);
        }
        catch (JsonException)
        {
            return false;
        }

        if (result is null || result.Plan.Count == 0)
        {
            return false;
        }

        string[] tasks = result.Plan
            .Select(static item => item.Step)
            .Where(static step => !string.IsNullOrWhiteSpace(step))
            .ToArray();

        if (tasks.Length == 0)
        {
            return false;
        }

        int completedTaskCount = Math.Min(
            result.CompletedTaskCount,
            tasks.Length);
        progress = new ExecutionPlanProgress(tasks, completedTaskCount);
        return true;
    }

    private async Task<ConversationResponse?> SendAndMapResponseAsync(
        string apiKey,
        ReplSessionContext session,
        IReadOnlyList<ConversationRequestMessage> messages,
        string? systemPrompt,
        IReadOnlyList<ToolDefinition> availableTools,
        ConversationSettings settings,
        CancellationTokenSource timeoutSource,
        CancellationToken cancellationToken)
    {
        string? requestSystemPrompt = systemPrompt;

        for (int attempt = 0; attempt <= EmptyResponseRetryLimit; attempt++)
        {
            ConversationProviderPayload providerPayload = await SendProviderRequestAsync(
                apiKey,
                session,
                messages,
                requestSystemPrompt,
                availableTools,
                settings,
                timeoutSource,
                cancellationToken);

            try
            {
                return _responseMapper.Map(providerPayload);
            }
            catch (ConversationResponseException exception)
                when (exception.IsRetryableEmptyResponse && attempt < EmptyResponseRetryLimit)
            {
                requestSystemPrompt = CreateEmptyResponseRetrySystemPrompt(systemPrompt);
            }
            catch (ConversationResponseException exception) when (exception.IsRetryableEmptyResponse)
            {
                return null;
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
        }

        return null;
    }

    private async Task<ConversationProviderPayload> SendProviderRequestAsync(
        string apiKey,
        ReplSessionContext session,
        IReadOnlyList<ConversationRequestMessage> messages,
        string? systemPrompt,
        IReadOnlyList<ToolDefinition> availableTools,
        ConversationSettings settings,
        CancellationTokenSource timeoutSource,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _providerClient.SendAsync(
                new ConversationProviderRequest(
                    session.ProviderProfile,
                    apiKey,
                    session.ActiveModelId,
                    messages.ToArray(),
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
    }

    private static PhaseExecutionResult CreateEmptyResponseFallback(
        IReadOnlyList<ToolInvocationResult> executedToolResults,
        int totalCompletionTokens,
        bool hasReportedCompletionTokens)
    {
        return new PhaseExecutionResult(
            EmptyResponseFallbackMessage,
            executedToolResults,
            totalCompletionTokens,
            hasReportedCompletionTokens,
            isProviderEmptyResponseFallback: true);
    }

    private static ToolExecutionBatchResult? CreateBatchResult(
        IReadOnlyList<ToolInvocationResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results.Count == 0
            ? null
            : new ToolExecutionBatchResult(results.ToArray());
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

        public ExecutionPlanProgress Complete()
        {
            _completedTaskCount = _tasks.Count;
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
            IReadOnlyList<ToolInvocationResult> executedToolResults,
            int totalCompletionTokens,
            bool hasReportedCompletionTokens,
            bool isProviderEmptyResponseFallback = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assistantMessage);
            ArgumentNullException.ThrowIfNull(executedToolResults);

            AssistantMessage = assistantMessage.Trim();
            ExecutedToolResults = executedToolResults.ToArray();
            TotalCompletionTokens = totalCompletionTokens;
            HasReportedCompletionTokens = hasReportedCompletionTokens;
            IsProviderEmptyResponseFallback = isProviderEmptyResponseFallback;
        }

        public string AssistantMessage { get; }

        public IReadOnlyList<ToolInvocationResult> ExecutedToolResults { get; }

        public bool HasReportedCompletionTokens { get; }

        public bool IsProviderEmptyResponseFallback { get; }

        public int TotalCompletionTokens { get; }
    }
}
