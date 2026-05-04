using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class ThinkingCommandHandler : IReplCommandHandler
{
    private readonly IAgentConfigurationStore _configurationStore;

    public ThinkingCommandHandler(IAgentConfigurationStore configurationStore)
    {
        _configurationStore = configurationStore;
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
            return ReplCommandResult.Continue(
                $"Thinking: {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}. " +
                "Use /thinking on or /thinking off.");
        }

        string requestedMode = context.ArgumentText.Trim();
        string? normalizedMode;
        try
        {
            normalizedMode = ReasoningEffortOptions.NormalizeOrThrow(requestedMode);
        }
        catch (ArgumentException)
        {
            return ReplCommandResult.Continue(
                $"Unsupported thinking mode '{requestedMode}'. Supported values: {ReasoningEffortOptions.SupportedValuesText}.",
                ReplFeedbackKind.Error);
        }

        bool modeChanged = context.Session.SetReasoningEffort(normalizedMode);
        await SaveAsync(context.Session, cancellationToken);

        return ReplCommandResult.Continue(
            modeChanged
                ? $"Thinking turned {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}."
                : $"Thinking is already {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}.");
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
                session.ActiveProviderName),
            cancellationToken);
    }
}
