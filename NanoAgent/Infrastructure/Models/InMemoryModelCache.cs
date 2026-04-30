using NanoAgent.Application.Abstractions;
using NanoAgent.Domain.Models;
using System.Collections.Concurrent;

namespace NanoAgent.Infrastructure.Models;

internal sealed class InMemoryModelCache : IModelCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<AvailableModel>> GetOrCreateAsync(
        string cacheKey,
        TimeSpan cacheDuration,
        Func<CancellationToken, Task<IReadOnlyList<AvailableModel>>> valueFactory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (_entries.TryGetValue(cacheKey, out CacheEntry? cachedEntry) &&
            cachedEntry.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cachedEntry.Models;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_entries.TryGetValue(cacheKey, out cachedEntry) &&
                cachedEntry.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return cachedEntry.Models;
            }

            IReadOnlyList<AvailableModel> models = (await valueFactory(cancellationToken))
                .ToArray();

            CacheEntry newEntry = new(
                DateTimeOffset.UtcNow.Add(cacheDuration),
                models);

            _entries[cacheKey] = newEntry;
            return newEntry.Models;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record CacheEntry(
        DateTimeOffset ExpiresAtUtc,
        IReadOnlyList<AvailableModel> Models);
}
