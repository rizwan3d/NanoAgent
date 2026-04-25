namespace NanoAgent.Application.Models;

public sealed record LessonMemorySaveRequest(
    string Trigger,
    string Problem,
    string Lesson,
    IReadOnlyList<string>? Tags = null,
    string Kind = "lesson",
    string? ToolName = null,
    string? Command = null,
    string? FailureSignature = null,
    string? Fingerprint = null,
    bool IsFixed = false,
    string? FixSummary = null);

public sealed record LessonMemoryEditRequest(
    string Id,
    string? Trigger = null,
    string? Problem = null,
    string? Lesson = null,
    IReadOnlyList<string>? Tags = null,
    string? Kind = null,
    string? ToolName = null,
    string? Command = null,
    string? FailureSignature = null,
    string? Fingerprint = null,
    bool? IsFixed = null,
    string? FixSummary = null);
