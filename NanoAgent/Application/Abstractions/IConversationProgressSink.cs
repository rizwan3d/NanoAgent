using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IConversationProgressSink
{
    Task ReportAssistantReasoningAsync(
        string reasoningText,
        CancellationToken cancellationToken);

    Task ReportExecutionPlanAsync(
        ExecutionPlanProgress executionPlanProgress,
        CancellationToken cancellationToken);

    Task ReportToolCallsStartedAsync(
        IReadOnlyList<ConversationToolCall> toolCalls,
        CancellationToken cancellationToken);

    Task ReportToolResultsAsync(
        ToolExecutionBatchResult toolExecutionResult,
        CancellationToken cancellationToken);
}
