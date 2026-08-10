namespace StemCode.Infrastructure.Models;

internal interface IOpenAiCodexClientVersionProvider
{
    Task<string> GetClientVersionAsync(CancellationToken cancellationToken);
}
