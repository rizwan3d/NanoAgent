using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IConfirmationPrompt
{
    Task<bool> PromptAsync(ConfirmationPromptRequest request, CancellationToken cancellationToken);
}
