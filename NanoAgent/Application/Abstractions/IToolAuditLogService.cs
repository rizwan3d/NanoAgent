using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IToolAuditLogService
{
    Task RecordAsync(
        ConversationToolCall toolCall,
        ToolInvocationResult invocationResult,
        ReplSessionContext session,
        ConversationExecutionPhase executionPhase,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken);

    string GetStoragePath();
}
