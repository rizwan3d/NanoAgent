namespace NanoAgent.Application.Models;

public sealed record SessionTerminalCommand(
    DateTimeOffset ExecutedAtUtc,
    string Command,
    string WorkingDirectory,
    int ExitCode,
    string? StandardOutput,
    string? StandardError,
    bool Background = false,
    string? TerminalId = null,
    string? TerminalStatus = null);
