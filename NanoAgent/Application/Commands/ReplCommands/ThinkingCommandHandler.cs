using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class ThinkingCommandHandler : IReplCommandHandler
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly ISelectionPrompt _selectionPrompt;

    public ThinkingCommandHandler(
        IAgentConfigurationStore configurationStore,
        ISelectionPrompt selectionPrompt)
    {
        _configurationStore = configurationStore;
        _selectionPrompt = selectionPrompt;
    }

    public string CommandName => "thinking";

    public string Description => "Show or set thinking mode for subsequent prompts.";

    public string Usage => "/thinking [on|off]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return await PromptForThinkingAsync(context, cancellationToken);
        }

        string requestedMode = context.ArgumentText.Trim();
        string? normalizedMode;
        try
        {
            normalizedMode = ThinkingModeOptions.NormalizeOrThrow(requestedMode);
        }
        catch (ArgumentException)
        {
            return ReplCommandResult.Continue(
                $"Unsupported thinking mode '{requestedMode}'. Supported values: {ThinkingModeOptions.SupportedValuesText}.",
                ReplFeedbackKind.Error);
        }

        bool modeChanged = context.Session.SetThinkingMode(normalizedMode);
        await SaveAsync(context.Session, cancellationToken);

        return ReplCommandResult.Continue(
            modeChanged
                ? $"Thinking turned {ThinkingModeOptions.Format(context.Session.ThinkingMode)}."
                : $"Thinking is already {ThinkingModeOptions.Format(context.Session.ThinkingMode)}.");
    }

    private async Task<ReplCommandResult> PromptForThinkingAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        string selectedMode;
        try
        {
            selectedMode = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<string>(
                    "Choose thinking mode",
                    [
                        new SelectionPromptOption<string>(
                            "On",
                            ThinkingModeOptions.On,
                            string.Equals(context.Session.ThinkingMode, ThinkingModeOptions.On, StringComparison.Ordinal)
                                ? "Currently active."
                                : "Use the model's default reasoning effort."),
                        new SelectionPromptOption<string>(
                            "Off",
                            ThinkingModeOptions.Off,
                            string.Equals(context.Session.ThinkingMode, ThinkingModeOptions.Off, StringComparison.Ordinal)
                                ? "Currently active."
                                : "Use lighter responses without extra thinking.")
                    ],
                    "Thinking mode applies to subsequent prompts. Esc to cancel.",
                    DefaultIndex: string.Equals(context.Session.ThinkingMode, ThinkingModeOptions.On, StringComparison.Ordinal)
                        ? 0
                        : 1,
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
            return ReplCommandResult.Continue(
                "Thinking selection cancelled.",
                ReplFeedbackKind.Warning);
        }

        bool modeChanged = context.Session.SetThinkingMode(selectedMode);
        await SaveAsync(context.Session, cancellationToken);

        return ReplCommandResult.Continue(
            modeChanged
                ? $"Thinking turned {ThinkingModeOptions.Format(context.Session.ThinkingMode)}."
                : $"Thinking is already {ThinkingModeOptions.Format(context.Session.ThinkingMode)}.");
    }

    private Task SaveAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        return _configurationStore.SaveAsync(
            new AgentConfiguration(
                session.ProviderProfile,
                session.ActiveModelId,
                session.ReasoningEffort,
                session.ActiveProviderName,
                session.ThinkingMode),
            cancellationToken);
    }
}
