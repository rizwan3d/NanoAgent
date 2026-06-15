using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IConversationProgressSink
{
    Task ReportAssistantMessageChunkAsync(
        string text,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

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

    // Optional capability: surfaces provider request retries (e.g. "Trying 1/10
    // (host not found)"). Sinks that do not present live progress ignore it.
    Task ReportProviderRetryAsync(
        ProviderRetryProgress providerRetryProgress,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
