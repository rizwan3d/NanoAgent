using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class ReasoningCommandHandler : IReplCommandHandler
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IInteractiveReasoningSelectionService _reasoningSelectionService;

    public ReasoningCommandHandler(
        IAgentConfigurationStore configurationStore,
        IInteractiveReasoningSelectionService reasoningSelectionService)
    {
        _configurationStore = configurationStore;
        _reasoningSelectionService = reasoningSelectionService;
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
            return await _reasoningSelectionService.SelectAsync(context.Session, cancellationToken);
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
