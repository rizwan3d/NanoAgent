using FinalAgent.Domain.Models;

namespace FinalAgent.Application.Models;

public sealed record ReplSessionContext(
    AgentProviderProfile ProviderProfile,
    string SelectedModelId)
{
    public string ProviderName => ProviderProfile.ProviderKind.ToDisplayName();
}
