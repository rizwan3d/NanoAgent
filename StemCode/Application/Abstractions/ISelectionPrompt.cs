using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface ISelectionPrompt
{
    Task<T> PromptAsync<T>(SelectionPromptRequest<T> request, CancellationToken cancellationToken);
}
