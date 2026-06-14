using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Backend;

internal sealed class PersistingConversationProgressSink : IConversationProgressSink
{
    private readonly IConversationProgressSink _inner;
    private readonly ISessionEventLogService _sessionEventLogService;
    private readonly ReplSessionContext _session;

    public PersistingConversationProgressSink(
        IConversationProgressSink inner,
        ISessionEventLogService sessionEventLogService,
        ReplSessionContext session)
    {
        _inner = inner;
        _sessionEventLogService = sessionEventLogService;
        _session = session;
    }

    public async Task ReportAssistantReasoningAsync(
        string reasoningText,
        CancellationToken cancellationToken)
    {
        await _inner.ReportAssistantReasoningAsync(reasoningText, cancellationToken);
        await _sessionEventLogService.RecordAssistantReasoningAsync(
            _session,
            reasoningText,
            cancellationToken);
    }

    public async Task ReportExecutionPlanAsync(
        ExecutionPlanProgress executionPlanProgress,
        CancellationToken cancellationToken)
    {
        await _inner.ReportExecutionPlanAsync(executionPlanProgress, cancellationToken);
        await _sessionEventLogService.RecordExecutionPlanAsync(
            _session,
            executionPlanProgress,
            cancellationToken);
    }

    public async Task ReportToolCallsStartedAsync(
        IReadOnlyList<ConversationToolCall> toolCalls,
        CancellationToken cancellationToken)
    {
        await _inner.ReportToolCallsStartedAsync(toolCalls, cancellationToken);

        foreach (ConversationToolCall toolCall in toolCalls)
        {
            await _sessionEventLogService.RecordToolCallRequestedAsync(
                _session,
                toolCall,
                cancellationToken);
        }
    }

    public Task ReportProviderRetryAsync(
        ProviderRetryProgress providerRetryProgress,
        CancellationToken cancellationToken)
    {
        // Retries are transient transport noise rather than conversation state,
        // so forward them to the live UI without writing to the session log.
        return _inner.ReportProviderRetryAsync(providerRetryProgress, cancellationToken);
    }

    public async Task ReportToolResultsAsync(
        ToolExecutionBatchResult toolExecutionResult,
        CancellationToken cancellationToken)
    {
        await _inner.ReportToolResultsAsync(toolExecutionResult, cancellationToken);

        foreach (ToolInvocationResult invocationResult in toolExecutionResult.Results)
        {
            await _sessionEventLogService.RecordToolResultAsync(
                _session,
                invocationResult,
                cancellationToken);
        }
    }
}
