namespace NanoAgent.Application.Abstractions;

public interface IGitHubCopilotAuthenticator
{
    Task<string> AuthenticateAsync(CancellationToken cancellationToken);
}
