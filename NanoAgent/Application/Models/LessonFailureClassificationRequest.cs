namespace NanoAgent.Application.Models;

public sealed record LessonFailureClassificationRequest(
    string ToolName,
    string Trigger,
    string Problem,
    string AttemptSummary,
    string? Command,
    string? FailureSignature,
    IReadOnlyList<string> Tags);

