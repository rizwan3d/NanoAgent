using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IToolExecutionPipeline
{
    Task<ToolExecutionBatchResult> ExecuteAsync(
        IReadOnlyList<ConversationToolCall> toolCalls,
        ReplSessionContext session,
        ConversationExecutionPhase executionPhase,
        IReadOnlySet<string> allowedToolNames,
        CancellationToken cancellationToken);
}
