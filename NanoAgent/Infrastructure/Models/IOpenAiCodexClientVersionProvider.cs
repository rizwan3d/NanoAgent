namespace NanoAgent.Infrastructure.Models;

internal interface IOpenAiCodexClientVersionProvider
{
    Task<string> GetClientVersionAsync(CancellationToken cancellationToken);
}
