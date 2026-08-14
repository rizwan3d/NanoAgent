using StemCode.Domain.Models;

namespace StemCode.Application.Models;

public sealed record SavedProviderConfiguration(
    string Name,
    AgentProviderProfile ProviderProfile,
    string? PreferredModelId);
