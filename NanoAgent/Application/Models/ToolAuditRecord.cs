namespace NanoAgent.Application.Models;

public sealed record ToolAuditRecord(
    DateTimeOffset TimestampUtc,
    string SessionId,
    string AgentProfileName,
    string ExecutionPhase,
    string ToolCallId,
    string ToolName,
    string Status,
    long DurationMilliseconds,
    string WorkingDirectory,
    string ArgumentsJson,
    string ResultMessage,
    string ResultJson);
