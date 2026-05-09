namespace NanoAgent.Application.Tools.Models;

public sealed record BackgroundTerminalInfo(
    string Id,
    string SessionId,
    string Command,
    string WorkingDirectory,
    string Status,
    int? ExitCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

public sealed record BackgroundTerminalListResult(
    IReadOnlyList<BackgroundTerminalInfo> Terminals);
