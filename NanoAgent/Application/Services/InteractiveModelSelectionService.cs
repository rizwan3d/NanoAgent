using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Services;

internal sealed class InteractiveModelSelectionService : IInteractiveModelSelectionService
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IModelActivationService _modelActivationService;
    private readonly IInteractiveReasoningSelectionService _reasoningSelectionService;
    private readonly ISelectionPrompt _selectionPrompt;

    public InteractiveModelSelectionService(
        ISelectionPrompt selectionPrompt,
        IModelActivationService modelActivationService,
        IAgentConfigurationStore configurationStore,
        IInteractiveReasoningSelectionService reasoningSelectionService)
    {
        _selectionPrompt = selectionPrompt;
        _modelActivationService = modelActivationService;
        _configurationStore = configurationStore;
        _reasoningSelectionService = reasoningSelectionService;
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
                    session.ActiveProviderName,
                    session.ThinkingMode),
                cancellationToken);
        }

        switch (result.Status)
        {
            case ModelActivationStatus.Switched:
                return await ChainReasoningSelectionAsync(
                    session,
                    $"Active model switched to '{result.ResolvedModelId.ToDisplayName()}'.",
                    cancellationToken);
            case ModelActivationStatus.AlreadyActive:
                return await ChainReasoningSelectionAsync(
                    session,
                    $"Already using '{result.ResolvedModelId.ToDisplayName()}'.",
                    cancellationToken);
            case ModelActivationStatus.Ambiguous:
                return ReplCommandResult.Continue(
                    "Selected model is ambiguous. Matches: " + string.Join(", ", result.CandidateModelIds.Select(id => id.ToDisplayName())),
                    ReplFeedbackKind.Error);
            default:
                return ReplCommandResult.Continue(
                    $"Selected model '{selectedModelId.ToDisplayName()}' is not available.",
                    ReplFeedbackKind.Error);
        }
    }

    private async Task<ReplCommandResult> ChainReasoningSelectionAsync(
        ReplSessionContext session,
        string modelMessage,
        CancellationToken cancellationToken)
    {
        ReplCommandResult reasoningResult = await _reasoningSelectionService.SelectAsync(
            session,
            cancellationToken);

        string combinedMessage = string.IsNullOrWhiteSpace(reasoningResult.Message)
            ? modelMessage
            : $"{modelMessage} {reasoningResult.Message}";

        ReplFeedbackKind feedbackKind = reasoningResult.FeedbackKind == ReplFeedbackKind.Error
            ? ReplFeedbackKind.Error
            : ReplFeedbackKind.Info;

        return ReplCommandResult.Continue(combinedMessage, feedbackKind);
    }

    private static SelectionPromptOption<string>[] CreateOptions(
        ReplSessionContext session)
    {
        return session.AvailableModelIds
            .Select(modelId => new SelectionPromptOption<string>(
                modelId.ToDisplayName(),
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
