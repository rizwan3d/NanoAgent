using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface ILessonMemoryService
{
    Task<LessonMemoryEntry> SaveAsync(
        LessonMemorySaveRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LessonMemoryEntry>> SearchAsync(
        string query,
        int limit,
        bool includeFixed,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LessonMemoryEntry>> ListAsync(
        int limit,
        bool includeFixed,
        CancellationToken cancellationToken);

    Task<LessonMemoryEntry?> EditAsync(
        LessonMemoryEditRequest request,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken);

    Task<string?> CreatePromptAsync(
        string query,
        CancellationToken cancellationToken);

    Task ObserveToolResultAsync(
        ConversationToolCall toolCall,
        ToolInvocationResult invocationResult,
        CancellationToken cancellationToken);

    string GetStoragePath();
}
