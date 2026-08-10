namespace StemCode.Domain.Models;

public sealed record AgentProviderProfile(
    ProviderKind ProviderKind,
    string? BaseUrl);
