namespace FinalAgent.Infrastructure.Secrets;

internal interface IPlatformCredentialStore
{
    Task<string?> LoadAsync(SecretReference secretReference, CancellationToken cancellationToken);

    Task SaveAsync(
        SecretReference secretReference,
        string secretValue,
        CancellationToken cancellationToken);
}
