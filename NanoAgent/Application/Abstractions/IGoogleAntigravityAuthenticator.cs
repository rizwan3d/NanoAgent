namespace NanoAgent.Application.Abstractions;

public interface IGoogleAntigravityAuthenticator
{
    Task<string> AuthenticateAsync(CancellationToken cancellationToken);
}
