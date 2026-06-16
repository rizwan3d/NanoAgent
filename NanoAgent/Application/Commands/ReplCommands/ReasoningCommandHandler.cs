using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class ReasoningCommandHandler : IReplCommandHandler
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly ISelectionPrompt _selectionPrompt;

    public ReasoningCommandHandler(
        IAgentConfigurationStore configurationStore,
        ISelectionPrompt selectionPrompt)
    {
        _configurationStore = configurationStore;
        _selectionPrompt = selectionPrompt;
    }

    public string CommandName => "reasoning";

    public string Description => "Show or set provider reasoning effort for subsequent prompts.";

    public string Usage => "/reasoning [show|<none|minimal|low|medium|high|xhigh|max>]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return await PromptForReasoningAsync(context, cancellationToken);
        }

        if (string.Equals(context.ArgumentText.Trim(), "show", StringComparison.OrdinalIgnoreCase))
        {
            return ReplCommandResult.Continue(
                $"Reasoning effort: {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}. " +
                $"Thinking: {ThinkingModeOptions.Format(context.Session.ThinkingMode)}.");
        }

        string requestedEffort = NormalizeRequestedEffort(context.ArgumentText);
        string? normalizedEffort;
        try
        {
            normalizedEffort = ReasoningEffortOptions.NormalizeOrThrow(requestedEffort);
        }
        catch (ArgumentException)
        {
            return ReplCommandResult.Continue(
                $"Unsupported reasoning effort '{requestedEffort}'. Supported values: {ReasoningEffortOptions.SupportedValuesText}.",
                ReplFeedbackKind.Error);
        }

        bool effortChanged = context.Session.SetReasoningEffort(normalizedEffort);
        if (!string.Equals(normalizedEffort, ReasoningEffortOptions.None, StringComparison.Ordinal))
        {
            context.Session.SetThinkingMode(ThinkingModeOptions.On);
        }

        await SaveAsync(context.Session, cancellationToken);

        return ReplCommandResult.Continue(
            effortChanged
                ? $"Reasoning effort set to {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}."
                : $"Reasoning effort is already {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}.");
    }

    private async Task<ReplCommandResult> PromptForReasoningAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        string selectedEffort;
        try
        {
            var options = new List<SelectionPromptOption<string>>();
            string? currentEffort = context.Session.ReasoningEffort;
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

        bool effortChanged = context.Session.SetReasoningEffort(selectedEffort);
        if (!string.Equals(selectedEffort, ReasoningEffortOptions.None, StringComparison.Ordinal))
        {
            context.Session.SetThinkingMode(ThinkingModeOptions.On);
        }

        await SaveAsync(context.Session, cancellationToken);

        return ReplCommandResult.Continue(
            effortChanged
                ? $"Reasoning effort set to {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}."
                : $"Reasoning effort is already {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}.");
    }

    private static string NormalizeRequestedEffort(string argumentText)
    {
        string[] parts = argumentText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            string.Equals(parts[0], "on", StringComparison.OrdinalIgnoreCase))
        {
            return parts[1];
        }

        return argumentText.Trim();
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
