using StemCode.Domain.Models;

namespace StemCode.Application.Abstractions;

public interface IModelCache
{
    Task<IReadOnlyList<AvailableModel>> GetOrCreateAsync(
        string cacheKey,
        TimeSpan cacheDuration,
        Func<CancellationToken, Task<IReadOnlyList<AvailableModel>>> valueFactory,
        CancellationToken cancellationToken);
}
