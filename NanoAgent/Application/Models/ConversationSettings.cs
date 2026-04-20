namespace NanoAgent.Application.Models;

public sealed record ConversationSettings(
    string? SystemPrompt,
    TimeSpan RequestTimeout,
    int MaxHistoryTurns,
    int MaxToolRoundsPerTurn);
