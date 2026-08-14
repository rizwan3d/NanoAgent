namespace StemCode.Application.Tools.Models;

public sealed record LessonMemoryToolResult(
    string Action,
    string Message,
    string StoragePath,
    IReadOnlyList<StemCode.Application.Models.LessonMemoryEntry> Lessons,
    int Count);
