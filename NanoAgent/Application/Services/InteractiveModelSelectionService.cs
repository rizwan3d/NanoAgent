using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Services;

internal sealed class InteractiveModelSelectionService : IInteractiveModelSelectionService
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IModelActivationService _modelActivationService;
    private readonly ISelectionPrompt _selectionPrompt;

    public InteractiveModelSelectionService(
        ISelectionPrompt selectionPrompt,
        IModelActivationService modelActivationService,
        IAgentConfigurationStore configurationStore)
    {
        _selectionPrompt = selectionPrompt;
        _modelActivationService = modelActivationService;
        _configurationStore = configurationStore;
    }

    public async Task<ReplCommandResult> SelectAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        if (session.AvailableModelIds.Count == 0)
        {
            return ReplCommandResult.Continue(
                "No models are available in the current session.",
                ReplFeedbackKind.Error);
        }

        string selectedModelId;
        try
        {
            selectedModelId = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<string>(
                    "Choose active model",
                    CreateOptions(session),
                    "Select the model to use for subsequent prompts.",
                    DefaultIndex: GetDefaultIndex(session),
                    AllowCancellation: true,
                    AutoSelectAfter: null),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
            return ReplCommandResult.Continue(
                "Model selection cancelled.",
                ReplFeedbackKind.Warning);
        }

        ModelActivationResult result = _modelActivationService.Resolve(
            session,
            selectedModelId);

        if (result.Status == ModelActivationStatus.Switched &&
            !string.IsNullOrWhiteSpace(result.ResolvedModelId))
        {
            await _configurationStore.SaveAsync(
                new AgentConfiguration(
                    session.ProviderProfile,
                    result.ResolvedModelId,
                    session.ReasoningEffort,
                    session.ActiveProviderName),
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
                    "Selected model is ambiguous. Matches: " + string.Join(", ", result.CandidateModelIds),
                    ReplFeedbackKind.Error),
            _ =>
                ReplCommandResult.Continue(
                    $"Selected model '{selectedModelId}' is not available.",
                    ReplFeedbackKind.Error)
        };
    }

    private static SelectionPromptOption<string>[] CreateOptions(
        ReplSessionContext session)
    {
        return session.AvailableModelIds
            .Select(modelId => new SelectionPromptOption<string>(
                modelId,
                modelId,
                string.Equals(modelId, session.ActiveModelId, StringComparison.Ordinal)
                    ? "Currently active."
                    : null))
            .ToArray();
    }

    private static int GetDefaultIndex(
        ReplSessionContext session)
    {
        int activeIndex = session.AvailableModelIds
            .ToList()
            .FindIndex(modelId => string.Equals(modelId, session.ActiveModelId, StringComparison.Ordinal));

        return activeIndex < 0 ? 0 : activeIndex;
    }
}
