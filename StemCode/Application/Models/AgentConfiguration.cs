using StemCode.Domain.Models;

namespace StemCode.Application.Models;

public sealed record AgentConfiguration(
    AgentProviderProfile ProviderProfile,
    string? PreferredModelId,
    string? ReasoningEffort = null,
    string? ActiveProviderName = null,
    string? ThinkingMode = null);
