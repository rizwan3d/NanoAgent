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
                $"Thinking: {ThinkingModeOptions.Format(context.Session.ThinkingMode)}. " +
                "Use /thinking on or /thinking off.");
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
