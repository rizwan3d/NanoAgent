namespace NanoAgent.Application.Models;

public sealed record SessionEventRecord(
    DateTimeOffset TimestampUtc,
    string SectionId,
    string? ParentSessionId,
    string EventType,
    string AgentProfileName,
    string ModelId,
    string WorkingDirectory,
    string? Text = null,
    string? ToolCallId = null,
    string? ToolName = null,
    string? ToolArgumentsJson = null,
    string? ToolStatus = null,
    string? ToolMessage = null,
    string? ToolResultJson = null,
    string? ErrorType = null);
