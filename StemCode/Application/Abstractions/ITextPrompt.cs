using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface ITextPrompt
{
    Task<string> PromptAsync(TextPromptRequest request, CancellationToken cancellationToken);
}
