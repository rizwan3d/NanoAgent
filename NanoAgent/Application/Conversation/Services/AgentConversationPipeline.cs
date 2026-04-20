using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Conversation.Serialization;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Logging;
using NanoAgent.Application.Models;
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
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(settings.RequestTimeout);
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        string normalizedInput = input.Trim();
        List<ConversationRequestMessage> messages =
        [
            .. session.GetConversationHistory(settings.MaxHistoryTurns),
            ConversationRequestMessage.User(normalizedInput)
        ];
        List<ToolInvocationResult> executedToolResults = [];
        int totalCompletionTokens = 0;
        bool hasReportedCompletionTokens = false;

        ApplicationLogMessages.ConversationRequestStarted(
            _logger,
            session.ProviderName,
            session.ActiveModelId);

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
                        settings.SystemPrompt,
                        _toolRegistry.GetToolDefinitions()),
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
                    cancellationToken);

                ApplicationLogMessages.ConversationToolHandoffCompleted(_logger);
                executedToolResults.AddRange(toolExecutionResult.Results);

                await progressSink.ReportToolResultsAsync(
                    toolExecutionResult,
                    cancellationToken);

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

            ApplicationLogMessages.ConversationAssistantMessageReceived(_logger);
            session.AddConversationTurn(normalizedInput, response.AssistantMessage);

            return ConversationTurnResult.AssistantMessage(
                response.AssistantMessage,
                executedToolResults.Count == 0
                    ? null
                    : new ToolExecutionBatchResult(executedToolResults.ToArray()),
                CreateMetrics(
                    startedAt,
                    response.AssistantMessage,
                    hasReportedCompletionTokens ? totalCompletionTokens : null));
        }

        throw new ConversationResponseException(
            $"The provider requested too many sequential tool rounds without producing a final assistant message. " +
            $"Configured limit: {settings.MaxToolRoundsPerTurn} round(s).");
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

        public Task ReportToolResultsAsync(
            ToolExecutionBatchResult toolExecutionResult,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
