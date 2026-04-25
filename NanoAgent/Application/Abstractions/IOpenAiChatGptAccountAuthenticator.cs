namespace NanoAgent.Application.Abstractions;

public interface IOpenAiChatGptAccountAuthenticator
{
    Task<string> AuthenticateAsync(CancellationToken cancellationToken);
}
