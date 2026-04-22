namespace NanoAgent.Application.Models;

public sealed record SessionSummary(
    string SessionId,
    string Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string ProviderName,
    string ActiveModelId,
    string ProfileName);
