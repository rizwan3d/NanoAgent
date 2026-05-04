using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Models;

public sealed record AgentConfiguration(
    AgentProviderProfile ProviderProfile,
    string? PreferredModelId,
    string? ReasoningEffort = null,
    string? ActiveProviderName = null);
