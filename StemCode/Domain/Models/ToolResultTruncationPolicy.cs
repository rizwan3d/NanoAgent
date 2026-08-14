namespace StemCode.Domain.Models;

public sealed record ToolResultTruncationPolicy(
    string Mode,
    int Limit);
