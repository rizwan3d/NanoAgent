using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Configuration;

namespace NanoAgent.Infrastructure.Secrets;

internal sealed class BudgetControlsSecretStore : IBudgetControlsSecretStore
{
    private const string CloudAuthKeyAccountName = "budget-controls-cloud-auth-key";

    private readonly IPlatformCredentialStore _platformCredentialStore;

    public BudgetControlsSecretStore(IPlatformCredentialStore platformCredentialStore)
    {
        _platformCredentialStore = platformCredentialStore;
    }

    public Task<string?> LoadCloudAuthKeyAsync(CancellationToken cancellationToken)
    {
        return _platformCredentialStore.LoadAsync(
            BuildCloudAuthKeyReference(),
            cancellationToken);
    }

    public Task SaveCloudAuthKeyAsync(
        string authKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authKey);

        return _platformCredentialStore.SaveAsync(
            BuildCloudAuthKeyReference(),
            authKey.Trim(),
            cancellationToken);
    }

    private static SecretReference BuildCloudAuthKeyReference()
    {
        return new SecretReference(
            ApplicationIdentity.ProductName,
            CloudAuthKeyAccountName,
            $"{ApplicationIdentity.ProductName} budget controls cloud auth key");
    }
}
