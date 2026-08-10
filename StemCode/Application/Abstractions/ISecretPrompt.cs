using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface ISecretPrompt
{
    Task<string> PromptAsync(SecretPromptRequest request, CancellationToken cancellationToken);
}
