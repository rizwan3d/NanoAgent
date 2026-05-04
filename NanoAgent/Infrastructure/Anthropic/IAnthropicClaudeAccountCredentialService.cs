namespace NanoAgent.Infrastructure.Anthropic;

internal interface IAnthropicClaudeAccountCredentialService
{
    Task<AnthropicClaudeAccountResolvedCredential> ResolveAsync(
        string storedCredentials,
        bool forceRefresh,
        CancellationToken cancellationToken);
}

internal sealed record AnthropicClaudeAccountResolvedCredential(string AccessToken);
