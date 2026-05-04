using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Models;

public sealed record SavedProviderConfiguration(
    string Name,
    AgentProviderProfile ProviderProfile,
    string? PreferredModelId);
