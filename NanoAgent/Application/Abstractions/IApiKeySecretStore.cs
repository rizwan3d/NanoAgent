namespace NanoAgent.Application.Abstractions;

public interface IApiKeySecretStore
{
    Task<string?> LoadAsync(CancellationToken cancellationToken);

    Task<string?> LoadAsync(string? providerName, CancellationToken cancellationToken)
    {
        return LoadAsync(cancellationToken);
    }

    Task SaveAsync(string apiKey, CancellationToken cancellationToken);

    Task SaveAsync(string? providerName, string apiKey, CancellationToken cancellationToken)
    {
        return SaveAsync(apiKey, cancellationToken);
    }
}
