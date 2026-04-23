namespace NanoAgent.Application.Models;

public sealed record SessionFileContext(
    string Path,
    string Activity,
    DateTimeOffset ObservedAtUtc,
    string Summary);
