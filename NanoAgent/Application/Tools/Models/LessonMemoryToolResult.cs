namespace NanoAgent.Application.Tools.Models;

public sealed record LessonMemoryToolResult(
    string Action,
    string Message,
    string StoragePath,
    IReadOnlyList<NanoAgent.Application.Models.LessonMemoryEntry> Lessons,
    int Count);
