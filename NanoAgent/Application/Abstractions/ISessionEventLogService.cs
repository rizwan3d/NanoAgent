using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface ISessionEventLogService
{
    string GetStoragePath(string sectionId);

    Task RecordUserInputAsync(
        ReplSessionContext session,
        string input,
        CancellationToken cancellationToken);

    Task RecordAssistantReasoningAsync(
        ReplSessionContext session,
        string reasoningText,
        CancellationToken cancellationToken);

    Task RecordAssistantOutputAsync(
        ReplSessionContext session,
        string outputText,
        CancellationToken cancellationToken);

    Task RecordToolCallRequestedAsync(
        ReplSessionContext session,
        ConversationToolCall toolCall,
        CancellationToken cancellationToken);

    Task RecordToolResultAsync(
        ReplSessionContext session,
        ToolInvocationResult invocationResult,
        CancellationToken cancellationToken);

    Task RecordExecutionPlanAsync(
        ReplSessionContext session,
        ExecutionPlanProgress executionPlanProgress,
        CancellationToken cancellationToken);

    Task RecordTurnFailureAsync(
        ReplSessionContext session,
        string input,
        Exception exception,
        CancellationToken cancellationToken);
}
