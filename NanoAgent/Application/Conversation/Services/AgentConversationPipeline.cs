using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Logging;
using NanoAgent.Application.Models;
using Microsoft.Extensions.Logging;

namespace NanoAgent.Application.Conversation.Services;

internal sealed class AgentConversationPipeline : IConversationPipeline
{
    private const int MaxProviderRoundsPerTurn = 8;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        string apiKey = await _secretStore.LoadAsync(cancellationToken)
            ?? throw new ConversationPipelineException(
                "Conversation cannot start because the API key is missing.");

        ConversationSettings settings = _configurationAccessor.GetSettings();
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(settings.RequestTimeout);
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        List<ConversationRequestMessage> messages =
        [
            ConversationRequestMessage.User(input.Trim())
        ];
        int totalCompletionTokens = 0;
        bool hasReportedCompletionTokens = false;

        ApplicationLogMessages.ConversationRequestStarted(
            _logger,
            session.ProviderName,
            session.ActiveModelId);

        for (int round = 0; round < MaxProviderRoundsPerTurn; round++)
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

                ToolExecutionBatchResult toolExecutionResult = await _toolExecutionPipeline.ExecuteAsync(
                    response.ToolCalls,
                    session,
                    cancellationToken);

                ApplicationLogMessages.ConversationToolHandoffCompleted(_logger);

                messages.Add(ConversationRequestMessage.AssistantToolCalls(
                    response.ToolCalls,
                    response.AssistantMessage));

                foreach (ToolInvocationResult invocationResult in toolExecutionResult.Results)
                {
                    messages.Add(ConversationRequestMessage.ToolResult(
                        invocationResult.ToolCallId,
                        invocationResult.Result.JsonResult));
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(response.AssistantMessage))
            {
                throw new ConversationResponseException(
                    "The provider response did not contain an assistant message or any tool calls.");
            }

            ApplicationLogMessages.ConversationAssistantMessageReceived(_logger);

            return ConversationTurnResult.AssistantMessage(
                response.AssistantMessage,
                CreateMetrics(
                    startedAt,
                    response.AssistantMessage,
                    hasReportedCompletionTokens ? totalCompletionTokens : null));
        }

        throw new ConversationResponseException(
            "The provider requested too many sequential tool rounds without producing a final assistant message.");
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
}
