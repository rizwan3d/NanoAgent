using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IModelDiscoveryService
{
    Task<ModelDiscoveryResult> DiscoverAndSelectAsync(CancellationToken cancellationToken);
}
