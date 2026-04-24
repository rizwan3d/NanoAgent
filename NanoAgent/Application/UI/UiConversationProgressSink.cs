using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.UI;

public sealed class UiConversationProgressSink : IConversationProgressSink
{
    private readonly IUiBridge _uiBridge;

    public UiConversationProgressSink(IUiBridge uiBridge)
    {
        _uiBridge = uiBridge;
    }

    public Task ReportExecutionPlanAsync(
        ExecutionPlanProgress executionPlanProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _uiBridge.ShowExecutionPlan(executionPlanProgress);
        return Task.CompletedTask;
    }

    public Task ReportToolCallsStartedAsync(
        IReadOnlyList<ConversationToolCall> toolCalls,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _uiBridge.ShowToolCalls(toolCalls);
        return Task.CompletedTask;
    }

    public Task ReportToolResultsAsync(
        ToolExecutionBatchResult toolExecutionResult,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _uiBridge.ShowToolResults(toolExecutionResult);
        return Task.CompletedTask;
    }
}
