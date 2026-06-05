namespace NanoAgent.Application.Abstractions;

public interface INanoAgentEnterpriseAuthenticator
{
    Task<string> AuthenticateAsync(
        string baseUrl,
        CancellationToken cancellationToken);
}
