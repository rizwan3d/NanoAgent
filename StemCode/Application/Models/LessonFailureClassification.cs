namespace StemCode.Application.Models;

public sealed record LessonFailureClassification(
    string Trigger,
    string Problem,
    string Lesson,
    IReadOnlyList<string> Tags);

