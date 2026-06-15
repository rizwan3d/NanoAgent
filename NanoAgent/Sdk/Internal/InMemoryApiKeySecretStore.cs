using NanoAgent.Application.Abstractions;

namespace NanoAgent.Sdk.Internal;

/// <summary>
/// Holds a programmatically supplied API key in memory. Unlike the default
/// platform-backed secret store, the key is never persisted, so embedding the
/// SDK leaves the user's saved credentials untouched.
/// </summary>
internal sealed class InMemoryApiKeySecretStore : IApiKeySecretStore
{
    private string? _apiKey;

    public InMemoryApiKeySecretStore(string? apiKey)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public Task<string?> LoadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_apiKey);
    }

    public Task<string?> LoadAsync(string? providerName, CancellationToken cancellationToken)
    {
        return Task.FromResult(_apiKey);
    }

    public Task SaveAsync(string apiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey.Trim();
        return Task.CompletedTask;
    }

    public Task SaveAsync(string? providerName, string apiKey, CancellationToken cancellationToken)
    {
        return SaveAsync(apiKey, cancellationToken);
    }
}
