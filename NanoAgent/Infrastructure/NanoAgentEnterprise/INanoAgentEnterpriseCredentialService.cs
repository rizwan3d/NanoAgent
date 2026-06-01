namespace NanoAgent.Infrastructure.NanoAgentEnterprise;

internal interface INanoAgentEnterpriseCredentialService
{
    bool CanResolve(string storedCredentials);

    Task<NanoAgentEnterpriseResolvedCredential> ResolveAsync(
        string storedCredentials,
        bool forceRefresh,
        CancellationToken cancellationToken);
}

internal sealed record NanoAgentEnterpriseResolvedCredential(string AccessToken);
