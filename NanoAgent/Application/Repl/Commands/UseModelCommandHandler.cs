using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Repl.Commands;

internal sealed class UseModelCommandHandler : IReplCommandHandler
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IModelActivationService _modelActivationService;

    public UseModelCommandHandler(
        IModelActivationService modelActivationService,
        IAgentConfigurationStore configurationStore)
    {
        _modelActivationService = modelActivationService;
        _configurationStore = configurationStore;
    }

    public string CommandName => "use";

    public string Description => "Switch the active model for subsequent prompts.";

    public string Usage => "/use <model>";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return ReplCommandResult.Continue(
                "Usage: /use <model>",
                ReplFeedbackKind.Error);
        }

        string requestedModel = context.ArgumentText.Trim();
        ModelActivationResult result = _modelActivationService.Resolve(
            context.Session,
            requestedModel);

        if (result.Status == ModelActivationStatus.Switched)
        {
            await _configurationStore.SaveAsync(
                new AgentConfiguration(
                    context.Session.ProviderProfile,
                    result.ResolvedModelId,
                    context.Session.ReasoningEffort),
                cancellationToken);
        }

        return result.Status switch
        {
            ModelActivationStatus.Switched =>
                ReplCommandResult.Continue(
                    $"Active model switched to '{result.ResolvedModelId}'."),
            ModelActivationStatus.AlreadyActive =>
                ReplCommandResult.Continue(
                    $"Already using '{result.ResolvedModelId}'."),
            ModelActivationStatus.Ambiguous =>
                ReplCommandResult.Continue(
                    "Model name is ambiguous. Matches: " + string.Join(", ", result.CandidateModelIds),
                    ReplFeedbackKind.Error),
            _ =>
                ReplCommandResult.Continue(
                    $"Model '{requestedModel}' is not available. Use /models to see valid choices.",
                    ReplFeedbackKind.Error)
        };
    }
}
