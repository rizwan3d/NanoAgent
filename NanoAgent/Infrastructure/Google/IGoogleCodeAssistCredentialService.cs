namespace NanoAgent.Infrastructure.Google;

internal interface IGoogleCodeAssistCredentialService
{
    Task<GoogleCodeAssistResolvedCredential> ResolveAsync(
        string storedCredentials,
        bool forceRefresh,
        CancellationToken cancellationToken);
}

internal sealed record GoogleCodeAssistResolvedCredential(
    string AccessToken,
    string? ProjectId,
    GoogleCodeAssistProvider Provider);

internal enum GoogleCodeAssistProvider
{
    GeminiCli,
    GoogleAntigravity
}
