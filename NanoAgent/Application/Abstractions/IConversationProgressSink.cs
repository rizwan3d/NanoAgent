using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IConversationProgressSink
{
    Task ReportToolCallsStartedAsync(
        IReadOnlyList<ConversationToolCall> toolCalls,
        CancellationToken cancellationToken);

    Task ReportToolResultsAsync(
        ToolExecutionBatchResult toolExecutionResult,
        CancellationToken cancellationToken);
}
