using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Services;

internal sealed class InteractiveReasoningSelectionService : IInteractiveReasoningSelectionService
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly ISelectionPrompt _selectionPrompt;

    public InteractiveReasoningSelectionService(
        ISelectionPrompt selectionPrompt,
        IAgentConfigurationStore configurationStore)
    {
        _selectionPrompt = selectionPrompt;
        _configurationStore = configurationStore;
    }

    public async Task<ReplCommandResult> SelectAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        string selectedEffort;
        try
        {
            var options = new List<SelectionPromptOption<string>>();
            string? currentEffort = session.ReasoningEffort;
            int defaultIndex = 0;

            foreach (string value in ReasoningEffortOptions.SupportedValues)
            {
                string description = string.Equals(currentEffort, value, StringComparison.Ordinal)
                    ? "Currently active."
                    : value switch
                    {
                        ReasoningEffortOptions.None => "No reasoning effort.",
                        ReasoningEffortOptions.Minimal => "Minimal reasoning effort.",
                        ReasoningEffortOptions.Low => "Low reasoning effort.",
                        ReasoningEffortOptions.Medium => "Medium reasoning effort.",
                        ReasoningEffortOptions.High => "High reasoning effort.",
                        ReasoningEffortOptions.XHigh => "Extra high reasoning effort.",
                        ReasoningEffortOptions.Max => "Maximum reasoning effort.",
                        _ => ""
                    };

                options.Add(new SelectionPromptOption<string>(
                    ReasoningEffortOptions.Format(value),
                    value,
                    description));

                if (string.Equals(currentEffort, value, StringComparison.Ordinal))
                {
                    defaultIndex = options.Count - 1;
                }
            }

            selectedEffort = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<string>(
                    "Choose reasoning effort",
                    options,
                    "Reasoning effort applies to subsequent prompts. Esc to cancel.",
                    DefaultIndex: defaultIndex,
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
            return ReplCommandResult.Continue(
                "Reasoning selection cancelled.",
                ReplFeedbackKind.Warning);
        }

        bool effortChanged = session.SetReasoningEffort(selectedEffort);
        if (!string.Equals(selectedEffort, ReasoningEffortOptions.None, StringComparison.Ordinal))
        {
            session.SetThinkingMode(ThinkingModeOptions.On);
        }

        await SaveAsync(session, cancellationToken);

        return ReplCommandResult.Continue(
            effortChanged
                ? $"Reasoning effort set to {ReasoningEffortOptions.Format(session.ReasoningEffort)}."
                : $"Reasoning effort is already {ReasoningEffortOptions.Format(session.ReasoningEffort)}.");
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
