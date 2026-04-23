using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Repl.Commands;

internal sealed class ThinkingCommandHandler : IReplCommandHandler
{
    private readonly IAgentConfigurationStore _configurationStore;

    public ThinkingCommandHandler(IAgentConfigurationStore configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public string CommandName => "thinking";

    public string Description => "Show or set thinking effort for subsequent prompts.";

    public string Usage => "/thinking [none|minimal|low|medium|high|xhigh|default]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return ReplCommandResult.Continue(
                $"Thinking effort: {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}. " +
                $"Use /thinking <{string.Join("|", ReasoningEffortOptions.SupportedValues)}> or /thinking default.");
        }

        string requestedEffort = context.ArgumentText.Trim();
        if (IsDefaultKeyword(requestedEffort))
        {
            bool changed = context.Session.ClearReasoningEffort();
            await SaveAsync(context.Session, cancellationToken);

            return ReplCommandResult.Continue(
                changed
                    ? "Thinking effort reset to provider default."
                    : "Thinking effort is already using provider default.");
        }

        string normalizedEffort;
        try
        {
            normalizedEffort = ReasoningEffortOptions.NormalizeOrThrow(requestedEffort)!;
        }
        catch (ArgumentException)
        {
            return ReplCommandResult.Continue(
                $"Unsupported thinking effort '{requestedEffort}'. Supported values: " +
                $"{ReasoningEffortOptions.SupportedValuesText}, default.",
                ReplFeedbackKind.Error);
        }

        bool effortChanged = context.Session.SetReasoningEffort(normalizedEffort);
        await SaveAsync(context.Session, cancellationToken);

        return ReplCommandResult.Continue(
            effortChanged
                ? $"Thinking effort set to '{normalizedEffort}'."
                : $"Already using thinking effort '{normalizedEffort}'.");
    }

    private Task SaveAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        return _configurationStore.SaveAsync(
            new AgentConfiguration(
                session.ProviderProfile,
                session.ActiveModelId,
                session.ReasoningEffort),
            cancellationToken);
    }

    private static bool IsDefaultKeyword(string value)
    {
        return string.Equals(value, "default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase);
    }
}
