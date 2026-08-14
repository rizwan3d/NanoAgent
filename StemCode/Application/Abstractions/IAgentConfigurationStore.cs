using StemCode.Application.Models;
using StemCode.Domain.Models;

namespace StemCode.Application.Abstractions;

public interface IAgentConfigurationStore
{
    Task<AgentConfiguration?> LoadAsync(CancellationToken cancellationToken);

    async Task<IReadOnlyList<SavedProviderConfiguration>> ListProvidersAsync(CancellationToken cancellationToken)
    {
        AgentConfiguration? configuration = await LoadAsync(cancellationToken);
        return configuration is null
            ? []
            : [new SavedProviderConfiguration(
                configuration.ActiveProviderName ?? configuration.ProviderProfile.ProviderKind.ToDisplayName(),
                configuration.ProviderProfile,
                configuration.PreferredModelId)];
    }

    Task SaveAsync(AgentConfiguration configuration, CancellationToken cancellationToken);

    Task SetActiveProviderAsync(string providerName, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This configuration store does not support provider switching.");
    }
}
