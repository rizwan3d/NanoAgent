namespace NanoAgent.Domain.Models;

public sealed record ModelContextMetadata(
    int ContextWindowTokens,
    int? MaxContextWindowTokens = null,
    int? AutoCompactTokenLimit = null,
    double? EffectiveContextWindowPercent = null,
    ToolResultTruncationPolicy? ToolResultTruncationPolicy = null,
    int? ToolOutputTokenLimit = null);
