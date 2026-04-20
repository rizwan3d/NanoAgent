using NanoAgent.Application.Models;

namespace NanoAgent.ConsoleHost.Terminal;

internal readonly record struct InteractiveSelectionPromptLayout(
    int PromptTop,
    int OptionsTop,
    int TotalLineCount);

internal interface IConsolePromptRenderer
{
    InteractiveSelectionPromptLayout WriteInteractiveSelectionPrompt<T>(SelectionPromptRequest<T> request, int selectedIndex);

    void RewriteSelectionOptions<T>(
        SelectionPromptRequest<T> request,
        int selectedIndex,
        InteractiveSelectionPromptLayout layout);

    void ClearInteractiveSelectionPrompt(InteractiveSelectionPromptLayout layout);

    void WriteFallbackSelectionPrompt<T>(SelectionPromptRequest<T> request);

    void WriteSecretPrompt(SecretPromptRequest request);

    void WriteStatus(StatusMessageKind kind, string message);

    void WriteTextPrompt(TextPromptRequest request);
}
