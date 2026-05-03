namespace NanoAgent.Application.Tools.Models;

public sealed record RepoMemoryDocumentResult(
    string Name,
    string Path,
    string Title,
    string Description,
    bool Exists,
    bool IsEmpty,
    int CharacterCount,
    string? Content = null);

public sealed record RepoMemoryToolResult(
    string Action,
    string Message,
    IReadOnlyList<RepoMemoryDocumentResult> Documents,
    int DocumentCount);
