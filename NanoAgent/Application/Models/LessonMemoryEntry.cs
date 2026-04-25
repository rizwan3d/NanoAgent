namespace NanoAgent.Application.Models;

public sealed record LessonMemoryEntry(
    string Id,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Kind,
    string Trigger,
    string Problem,
    string Lesson,
    string[] Tags,
    string? ToolName = null,
    string? Command = null,
    string? FailureSignature = null,
    string? Fingerprint = null,
    bool IsFixed = false,
    DateTimeOffset? FixedAtUtc = null,
    string? FixSummary = null);
