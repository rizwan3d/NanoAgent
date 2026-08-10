namespace StemCode.Application.Abstractions;

public interface IAnthropicClaudeAccountAuthenticator
{
    Task<string> AuthenticateAsync(CancellationToken cancellationToken);
}
