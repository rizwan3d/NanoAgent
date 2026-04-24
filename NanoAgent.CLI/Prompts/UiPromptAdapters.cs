using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.CLI;

public sealed class UiSelectionPrompt : ISelectionPrompt
{
    private readonly IUiBridge _uiBridge;

    public UiSelectionPrompt(IUiBridge uiBridge)
    {
        _uiBridge = uiBridge;
    }

    public Task<T> PromptAsync<T>(SelectionPromptRequest<T> request, CancellationToken cancellationToken)
    {
        return _uiBridge.RequestSelectionAsync(request, cancellationToken);
    }
}

public sealed class UiTextPrompt : ITextPrompt
{
    private readonly IUiBridge _uiBridge;

    public UiTextPrompt(IUiBridge uiBridge)
    {
        _uiBridge = uiBridge;
    }

    public Task<string> PromptAsync(TextPromptRequest request, CancellationToken cancellationToken)
    {
        return _uiBridge.RequestTextAsync(request, isSecret: false, cancellationToken);
    }
}

public sealed class UiSecretPrompt : ISecretPrompt
{
    private readonly IUiBridge _uiBridge;

    public UiSecretPrompt(IUiBridge uiBridge)
    {
        _uiBridge = uiBridge;
    }

    public Task<string> PromptAsync(SecretPromptRequest request, CancellationToken cancellationToken)
    {
        return _uiBridge.RequestTextAsync(
            new TextPromptRequest(
                request.Label,
                request.Description,
                DefaultValue: null,
                request.AllowCancellation),
            isSecret: true,
            cancellationToken);
    }
}

public sealed class UiConfirmationPrompt : IConfirmationPrompt
{
    private readonly ISelectionPrompt _selectionPrompt;

    public UiConfirmationPrompt(ISelectionPrompt selectionPrompt)
    {
        _selectionPrompt = selectionPrompt;
    }

    public Task<bool> PromptAsync(ConfirmationPromptRequest request, CancellationToken cancellationToken)
    {
        return _selectionPrompt.PromptAsync(
            new SelectionPromptRequest<bool>(
                request.Title,
                [
                    new SelectionPromptOption<bool>(
                        "Yes",
                        true,
                        "Continue with this action."),
                    new SelectionPromptOption<bool>(
                        "No",
                        false,
                        "Cancel and leave things unchanged.")
                ],
                request.Description,
                DefaultIndex: request.DefaultValue ? 0 : 1,
                request.AllowCancellation,
                AutoSelectAfter: null),
            cancellationToken);
    }
}
