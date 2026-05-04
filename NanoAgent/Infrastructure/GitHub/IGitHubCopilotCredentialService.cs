namespace NanoAgent.Infrastructure.GitHub;

internal interface IGitHubCopilotCredentialService
{
    Task<GitHubCopilotResolvedCredential> ResolveAsync(
        string storedCredentials,
        bool forceRefresh,
        CancellationToken cancellationToken);
}

internal sealed record GitHubCopilotResolvedCredential(
    string AccessToken,
    string? EnterpriseDomain,
    Uri BaseUri);
