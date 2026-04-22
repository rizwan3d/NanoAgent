namespace NanoAgent.Application.Models;

public sealed record SelectionPromptRequest<T>(
    string Title,
    IReadOnlyList<SelectionPromptOption<T>> Options,
    string? Description = null,
    int DefaultIndex = 0,
    bool AllowCancellation = true,
    TimeSpan? AutoSelectAfter = null);
