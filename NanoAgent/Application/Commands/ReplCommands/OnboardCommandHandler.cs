using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Commands;

namespace NanoAgent.Application.Commands;

internal sealed class OnboardCommandHandler : IReplCommandHandler
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IFirstRunOnboardingService _onboardingService;
    private readonly IModelDiscoveryService _modelDiscoveryService;

    public OnboardCommandHandler(
        IFirstRunOnboardingService onboardingService,
        IModelDiscoveryService modelDiscoveryService,
        IAgentConfigurationStore configurationStore)
    {
        _onboardingService = onboardingService;
        _modelDiscoveryService = modelDiscoveryService;
        _configurationStore = configurationStore;
    }

    public string CommandName => "onboard";

    public string Description => "Re-run provider onboarding and switch the active session to the new provider.";

    public string Usage => "/onboard";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return ReplCommandResult.Continue(
                "Usage: /onboard",
                ReplFeedbackKind.Error);
        }

        OnboardingResult onboardingResult;
        try
        {
            onboardingResult = await _onboardingService.ReconfigureAsync(cancellationToken);
        }
        catch (PromptCancelledException)
        {
            return ReplCommandResult.Continue(
                "Provider onboarding cancelled.",
                ReplFeedbackKind.Warning);
        }

        ModelDiscoveryResult modelResult;
        try
        {
            modelResult = await _modelDiscoveryService.DiscoverAndSelectAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is ModelDiscoveryException or ModelProviderException or HttpRequestException or InvalidOperationException)
        {
            return ReplCommandResult.Continue(
                $"Provider onboarding saved credentials, but validation failed: {exception.Message}",
                ReplFeedbackKind.Error);
        }

        string[] availableModelIds = modelResult.AvailableModels
            .Select(static model => model.Id)
            .ToArray();

        context.Session.ReplaceProviderConfiguration(
            onboardingResult.Profile,
            modelResult.SelectedModelId,
            availableModelIds);

        await _configurationStore.SaveAsync(
            new AgentConfiguration(
                context.Session.ProviderProfile,
                context.Session.ActiveModelId,
                context.Session.ReasoningEffort),
            cancellationToken);

        return ReplCommandResult.Continue(
            "Provider onboarding complete.\n" +
            $"Provider: {context.Session.ProviderName}\n" +
            $"Active model: {context.Session.ActiveModelId}\n" +
            $"Available models: {context.Session.AvailableModelIds.Count}\n" +
            "Use /models to inspect models or /use <model> to switch.");
    }
}
