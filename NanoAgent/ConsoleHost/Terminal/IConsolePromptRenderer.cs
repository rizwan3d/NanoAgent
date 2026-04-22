using NanoAgent.Application.Models;

namespace NanoAgent.ConsoleHost.Terminal;

internal readonly record struct InteractiveSelectionPromptLayout(
    int PromptTop,
    int OptionsTop,
    int TotalLineCount,
    int DefaultLineTop = -1);

internal interface IConsolePromptRenderer
{
    InteractiveSelectionPromptLayout WriteInteractiveSelectionPrompt<T>(
        SelectionPromptRequest<T> request,
        int selectedIndex,
        int? remainingAutoSelectSeconds = null);

    void RewriteSelectionOptions<T>(
        SelectionPromptRequest<T> request,
        int selectedIndex,
        InteractiveSelectionPromptLayout layout);

    void RewriteSelectionDefaultLine<T>(
        SelectionPromptRequest<T> request,
        InteractiveSelectionPromptLayout layout,
        int remainingAutoSelectSeconds);

    void ClearInteractiveSelectionPrompt(InteractiveSelectionPromptLayout layout);

    void WriteSecretPrompt(SecretPromptRequest request);

    void WriteStatus(StatusMessageKind kind, string message);

    void WriteTextPrompt(TextPromptRequest request);
}
