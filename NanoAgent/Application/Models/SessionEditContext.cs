namespace NanoAgent.Application.Models;

public sealed record SessionEditContext(
    DateTimeOffset EditedAtUtc,
    string Description,
    IReadOnlyList<string> Paths,
    int AddedLineCount,
    int RemovedLineCount);
