using StemCode.Domain.Models;

namespace StemCode.Application.Abstractions;

public interface IModelProviderClient
{
    Task<IReadOnlyList<AvailableModel>> GetAvailableModelsAsync(
        AgentProviderProfile providerProfile,
        string apiKey,
        CancellationToken cancellationToken);
}
