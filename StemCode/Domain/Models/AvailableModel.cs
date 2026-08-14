namespace StemCode.Domain.Models;

public sealed record AvailableModel(
    string Id,
    int? ContextWindowTokens = null,
    ModelContextMetadata? ContextMetadata = null);
