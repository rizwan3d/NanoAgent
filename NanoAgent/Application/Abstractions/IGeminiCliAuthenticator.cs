namespace NanoAgent.Application.Abstractions;

public interface IGeminiCliAuthenticator
{
    Task<string> AuthenticateAsync(CancellationToken cancellationToken);
}
