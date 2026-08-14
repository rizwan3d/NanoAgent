using StemCode.Application.Abstractions;
using StemCode.Application.Models;

namespace StemCode.Sdk.Internal;

/// <summary>
/// Holds a programmatically supplied provider configuration in memory so the
/// onboarding flow detects an existing, complete configuration and skips all
/// interactive prompts. Nothing is written to disk, so embedding a client never
/// disturbs the machine-wide StemCode configuration.
/// </summary>
internal sealed class InMemoryAgentConfigurationStore : IAgentConfigurationStore
{
    private AgentConfiguration _configuration;

    public InMemoryAgentConfigurationStore(AgentConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public Task<AgentConfiguration?> LoadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<AgentConfiguration?>(_configuration);
    }

    public Task SaveAsync(AgentConfiguration configuration, CancellationToken cancellationToken)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        return Task.CompletedTask;
    }

    public Task SetActiveProviderAsync(string providerName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        _configuration = _configuration with { ActiveProviderName = providerName.Trim() };
        return Task.CompletedTask;
    }
}
