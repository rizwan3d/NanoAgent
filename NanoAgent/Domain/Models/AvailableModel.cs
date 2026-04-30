namespace NanoAgent.Domain.Models;

public sealed record AvailableModel(
    string Id,
    int? ContextWindowTokens = null);
