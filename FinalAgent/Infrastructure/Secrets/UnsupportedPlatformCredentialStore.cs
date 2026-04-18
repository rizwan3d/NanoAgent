namespace FinalAgent.Infrastructure.Secrets;

internal sealed class UnsupportedPlatformCredentialStore : IPlatformCredentialStore
{
    public Task<string?> LoadAsync(SecretReference secretReference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<string?>(null);
    }

    public Task SaveAsync(
        SecretReference secretReference,
        string secretValue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretValue);
        cancellationToken.ThrowIfCancellationRequested();

        throw new SecretStorageException(
            "No secure operating-system credential store implementation is available for this platform.");
    }
}
