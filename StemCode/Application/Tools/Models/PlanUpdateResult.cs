namespace StemCode.Application.Tools.Models;

public sealed record PlanUpdateResult(
    string? Explanation,
    IReadOnlyList<PlanUpdateItem> Plan,
    int CompletedTaskCount,
    int InProgressTaskCount,
    int PendingTaskCount);
