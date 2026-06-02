namespace NanoAgent.Infrastructure.NanoAgentEnterprise;

internal interface INanoAgentEnterpriseCredentialService
{
    bool CanResolve(string storedCredentials);

    Task<string> AuthenticateAsync(
        string baseUrl,
        CancellationToken cancellationToken);

    Task<NanoAgentEnterpriseResolvedCredential> ResolveAsync(
        string storedCredentials,
        bool forceRefresh,
        CancellationToken cancellationToken);
}

internal sealed record NanoAgentEnterpriseResolvedCredential(string AccessToken);
