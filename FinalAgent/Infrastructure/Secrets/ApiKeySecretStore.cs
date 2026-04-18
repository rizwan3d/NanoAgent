using FinalAgent.Application.Abstractions;
using FinalAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FinalAgent.Infrastructure.Secrets;

internal sealed class ApiKeySecretStore : IApiKeySecretStore
{
    private const string ApiKeyAccountName = "default-api-key";

    private readonly ApplicationOptions _options;
    private readonly IPlatformCredentialStore _platformCredentialStore;

    public ApiKeySecretStore(
        IOptions<ApplicationOptions> options,
        IPlatformCredentialStore platformCredentialStore)
    {
        _options = options.Value;
        _platformCredentialStore = platformCredentialStore;
    }

    public Task<string?> LoadAsync(CancellationToken cancellationToken)
    {
        return _platformCredentialStore.LoadAsync(BuildReference(), cancellationToken);
    }

    public Task SaveAsync(string apiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return _platformCredentialStore.SaveAsync(
            BuildReference(),
            apiKey.Trim(),
            cancellationToken);
    }

    private SecretReference BuildReference()
    {
        return new SecretReference(
            _options.ProductName,
            ApiKeyAccountName,
            $"{_options.ProductName} API key");
    }
}
