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
    private const int RetryableProviderOutputRetryLimit = 3;
    private const int IncompletePlanFinalResponseRetryLimit = 1;
    private const string EmptyResponseRetryInstruction =
        """
        The previous provider response was empty even though it ended normally. This output was rejected by the runtime and was not saved.
        Continue the same task from the current conversation state and return either:
        - a valid call to an available tool if any work remains, or
        - a non-empty assistant message that actually completes or materially advances the work
        Do not return empty content, whitespace, or another tool-less empty response.
        """;
    private const string ProviderRecoveryRetryInstruction =
        """
        This is a recovery request for the same user turn.
        Continue the same task and return either:
        - a non-empty assistant message that materially advances the work, or
        - a valid call to an available tool
        """;
    private const string RawToolCallRetryInstruction =
        """
        The previous provider response exposed raw tool-call protocol text in assistant content instead of returning a structured tool call.
        Continue the same task and return either:
        - a normal assistant message with no tool-call protocol markers, or
        - a valid structured tool call to one of the available tools
        Do not write markers such as <|channel>call:, <tool_call|>, assistant/tool protocol text, or tool-call JSON inside assistant content.
        """;
    private const string IncompletePlanRetryInstruction =
        """
        The previous provider response tried to finish while the live update_plan still had in_progress or pending work.
        Continue the same task now by calling the appropriate available tools, or call update_plan to revise/complete the live plan if it no longer reflects the work.
        If the listed work is truly complete, call update_plan with every item completed before returning final text.
        Do not repeat the final answer until the live plan has no in_progress or pending work.
        """;

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

    private static string CreateProviderRetrySystemPrompt(
        string? systemPrompt,
        ConversationResponseException exception,
        int recoveryAttempt,
        int retryLimit)
    {
        string retryInstruction = exception switch
        {
            { IsRetryableRawToolCallResponse: true } => RawToolCallRetryInstruction,
            { IsRetryableIncompletePlanResponse: true } => IncompletePlanRetryInstruction,
            _ => EmptyResponseRetryInstruction
        };

        string recoveryInstruction =
            $"{ProviderRecoveryRetryInstruction.Trim()}{Environment.NewLine}" +
            $"Recovery attempt {recoveryAttempt} of {retryLimit}.";

        return string.IsNullOrWhiteSpace(systemPrompt)
            ? $"{recoveryInstruction}{Environment.NewLine}{Environment.NewLine}{retryInstruction}"
            : $"{systemPrompt.Trim()}{Environment.NewLine}{Environment.NewLine}{recoveryInstruction}{Environment.NewLine}{Environment.NewLine}{retryInstruction}";
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
        string? contribution = session.AgentProfile.SystemPrompt;
        string? statefulContext = session.CreateStatefulContextPrompt();
        string?[] promptSections =
        [
            basePrompt,
            contribution,
            statefulContext
        ];

        string[] normalizedSections = promptSections
            .Where(static section => !string.IsNullOrWhiteSpace(section))
            .Select(static section => section!.Trim())
            .ToArray();

        return normalizedSections.Length == 0
            ? null
            : string.Join(
                $"{Environment.NewLine}{Environment.NewLine}",
                normalizedSections);
    }

    private static int? GetCompletionTokens(PhaseExecutionResult phaseResult)
    {
        return phaseResult.HasReportedCompletionTokens
            ? phaseResult.TotalCompletionTokens
            : null;
    }

    private static string CreateToolFeedbackContent(
        ToolInvocationResult invocationResult,
        int consecutiveFailureCount)
    {
        ArgumentNullException.ThrowIfNull(invocationResult);

        using JsonDocument dataDocument = JsonDocument.Parse(invocationResult.Result.JsonResult);

        ToolFeedbackPayload payload = new(
            invocationResult.ToolName,
            invocationResult.Result.Status,
            invocationResult.Result.IsSuccess,
            consecutiveFailureCount,
            invocationResult.Result.Message,
            dataDocument.RootElement.Clone(),
            invocationResult.Result.RenderPayload);

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
        int consecutiveToolFailureCount = 0;
        int incompletePlanFinalResponseRetryCount = 0;
        int totalCompletionTokens = 0;
        bool hasReportedCompletionTokens = false;
        ExecutionPlanProgress? latestPlanProgress = null;
        string? phaseSystemPrompt = systemPrompt;

        for (int round = 0; IsWithinToolRoundLimit(round, settings.MaxToolRoundsPerTurn); round++)
        {
            ConversationResponse response = await SendAndMapResponseAsync(
                apiKey,
                session,
                messages,
                phaseSystemPrompt,
                availableTools,
                settings,
                timeoutSource,
                cancellationToken);

            if (response.CompletionTokens is > 0)
            {
                totalCompletionTokens += response.CompletionTokens.Value;
                hasReportedCompletionTokens = true;
            }

            if (response.HasToolCalls)
            {
                phaseSystemPrompt = systemPrompt;
                incompletePlanFinalResponseRetryCount = 0;

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

                ExecutionPlanProgress? reportedPlanUpdate = await ReportPlanUpdatesAsync(
                    toolExecutionResult,
                    progressSink,
                    cancellationToken);
                if (reportedPlanUpdate is not null)
                {
                    latestPlanProgress = reportedPlanUpdate;
                }

                await progressSink.ReportToolResultsAsync(
                    toolExecutionResult,
                    cancellationToken);

                if (executionPhase == ConversationExecutionPhase.Execution &&
                    executionPlanTracker is not null &&
                    reportedPlanUpdate is null)
                {
                    latestPlanProgress = executionPlanTracker.Advance();
                    await progressSink.ReportExecutionPlanAsync(
                        latestPlanProgress,
                        cancellationToken);
                }

                messages.Add(ConversationRequestMessage.AssistantToolCalls(
                    response.ToolCalls,
                    response.AssistantMessage));

                foreach (ToolInvocationResult invocationResult in toolExecutionResult.Results)
                {
                    consecutiveToolFailureCount = invocationResult.Result.IsSuccess
                        ? 0
                        : IncrementFailureCount(consecutiveToolFailureCount);

                    messages.Add(ConversationRequestMessage.ToolResult(
                        invocationResult.ToolCallId,
                        CreateToolFeedbackContent(invocationResult, consecutiveToolFailureCount)));
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(response.AssistantMessage))
            {
                throw new ConversationResponseException(
                    "The provider response did not contain an assistant message or any tool calls.");
            }

            if (HasIncompleteLivePlan(latestPlanProgress))
            {
                if (incompletePlanFinalResponseRetryCount < IncompletePlanFinalResponseRetryLimit)
                {
                    incompletePlanFinalResponseRetryCount++;
                    phaseSystemPrompt = CreateProviderRetrySystemPrompt(
                        systemPrompt,
                        CreateIncompletePlanException(),
                        incompletePlanFinalResponseRetryCount,
                        IncompletePlanFinalResponseRetryLimit);
                    continue;
                }

                latestPlanProgress = CompleteLivePlan(latestPlanProgress!);
                await progressSink.ReportExecutionPlanAsync(
                    latestPlanProgress,
                    cancellationToken);
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

    private static bool IsWithinToolRoundLimit(
        int completedToolRoundCount,
        int maxToolRoundsPerTurn)
    {
        return maxToolRoundsPerTurn <= 0 ||
            completedToolRoundCount < maxToolRoundsPerTurn;
    }

    private static async Task<ExecutionPlanProgress?> ReportPlanUpdatesAsync(
        ToolExecutionBatchResult toolExecutionResult,
        IConversationProgressSink progressSink,
        CancellationToken cancellationToken)
    {
        ExecutionPlanProgress? latestProgress = null;

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
            latestProgress = progress;
        }

        return latestProgress;
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

    private static int IncrementFailureCount(int currentFailureCount)
    {
        return currentFailureCount == int.MaxValue
            ? int.MaxValue
            : currentFailureCount + 1;
    }

    private async Task<ConversationResponse> SendAndMapResponseAsync(
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

        for (int attempt = 0; attempt <= RetryableProviderOutputRetryLimit; attempt++)
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
                ConversationResponse response = _responseMapper.Map(providerPayload)
                    ?? throw new ConversationResponseException(
                        "The provider response mapper returned no normalized response.");
                return response;
            }
            catch (ConversationResponseException exception)
                when (exception.IsRetryableProviderOutput && attempt < RetryableProviderOutputRetryLimit)
            {
                requestSystemPrompt = CreateProviderRetrySystemPrompt(
                    systemPrompt,
                    exception,
                    attempt + 1,
                    RetryableProviderOutputRetryLimit);
            }
            catch (ConversationResponseException exception) when (exception.IsRetryableProviderOutput)
            {
                throw CreateProviderOutputExhaustedException(exception);
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

        throw new ConversationResponseException(
            "The provider response recovery loop ended without producing a normalized response.");
    }

    private static bool HasIncompleteLivePlan(ExecutionPlanProgress? latestPlanProgress)
    {
        return latestPlanProgress is not null &&
            latestPlanProgress.CompletedTaskCount < latestPlanProgress.Tasks.Count;
    }

    private static ConversationResponseException CreateIncompletePlanException()
    {
        return new ConversationResponseException(
            "The provider returned a final assistant message while the live plan still had in-progress or pending work.",
            isRetryableIncompletePlanResponse: true);
    }

    private static ExecutionPlanProgress CompleteLivePlan(ExecutionPlanProgress progress)
    {
        return new ExecutionPlanProgress(progress.Tasks, progress.Tasks.Count);
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
                    availableTools,
                    session.ReasoningEffort),
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

    private static ConversationResponseException CreateProviderOutputExhaustedException(
        ConversationResponseException lastException)
    {
        return new ConversationResponseException(
            $"The provider returned unusable output after {RetryableProviderOutputRetryLimit + 1} request(s). " +
            $"Last provider issue: {lastException.Message}",
            lastException);
    }

    private static ToolExecutionBatchResult? CreateBatchResult(
        IReadOnlyList<ToolInvocationResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results.Count == 0
            ? null
            : new ToolExecutionBatchResult(results.ToArray());
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
            bool hasReportedCompletionTokens)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assistantMessage);
            ArgumentNullException.ThrowIfNull(executedToolResults);

            AssistantMessage = assistantMessage.Trim();
            ExecutedToolResults = executedToolResults.ToArray();
            TotalCompletionTokens = totalCompletionTokens;
            HasReportedCompletionTokens = hasReportedCompletionTokens;
        }

        public string AssistantMessage { get; }

        public IReadOnlyList<ToolInvocationResult> ExecutedToolResults { get; }

        public bool HasReportedCompletionTokens { get; }

        public int TotalCompletionTokens { get; }
    }
}
