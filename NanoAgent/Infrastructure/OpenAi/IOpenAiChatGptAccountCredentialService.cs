namespace NanoAgent.Infrastructure.OpenAi;

internal interface IOpenAiChatGptAccountCredentialService
{
    Task<OpenAiChatGptAccountResolvedCredential> ResolveAsync(
        string storedCredentials,
        bool forceRefresh,
        CancellationToken cancellationToken);
}

internal sealed record OpenAiChatGptAccountResolvedCredential(
    string AccessToken,
    string? AccountId);

