using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Presentation.Cli.Prompts;

internal sealed class ConsoleConfirmationPrompt : IConfirmationPrompt
{
    private readonly ISelectionPrompt _selectionPrompt;

    public ConsoleConfirmationPrompt(ISelectionPrompt selectionPrompt)
    {
        _selectionPrompt = selectionPrompt;
    }

    public Task<bool> PromptAsync(ConfirmationPromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        SelectionPromptRequest<bool> selectionRequest = new(
            request.Title,
            [
                new SelectionPromptOption<bool>("Yes", true, "Continue with this action."),
                new SelectionPromptOption<bool>("No", false, "Cancel and leave things unchanged.")
            ],
            request.Description,
            request.DefaultValue ? 0 : 1,
            request.AllowCancellation);

        return _selectionPrompt.PromptAsync(selectionRequest, cancellationToken);
    }
}
