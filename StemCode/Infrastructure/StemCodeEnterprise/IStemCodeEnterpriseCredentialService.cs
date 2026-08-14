namespace StemCode.Infrastructure.StemCodeEnterprise;

internal interface IStemCodeEnterpriseCredentialService
{
    bool CanResolve(string storedCredentials);

    Task<string> AuthenticateAsync(
        string baseUrl,
        CancellationToken cancellationToken);

    Task<StemCodeEnterpriseResolvedCredential> ResolveAsync(
        string storedCredentials,
        bool forceRefresh,
        CancellationToken cancellationToken);
}

internal sealed record StemCodeEnterpriseResolvedCredential(string AccessToken);
