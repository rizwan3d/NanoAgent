using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Commands;

internal sealed class ProviderCommandHandler : IReplCommandHandler
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IModelDiscoveryService _modelDiscoveryService;
    private readonly ISelectionPrompt _selectionPrompt;

    public ProviderCommandHandler(
        IAgentConfigurationStore configurationStore,
        IApiKeySecretStore secretStore,
        IModelDiscoveryService modelDiscoveryService,
        ISelectionPrompt selectionPrompt)
    {
        _configurationStore = configurationStore;
        _secretStore = secretStore;
        _modelDiscoveryService = modelDiscoveryService;
        _selectionPrompt = selectionPrompt;
    }

    public string CommandName => "provider";

    public string Description => "List saved providers or switch the active session to another saved provider.";

    public string Usage => "/provider [list|<name>]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<SavedProviderConfiguration> providers =
            await _configurationStore.ListProvidersAsync(cancellationToken);

        if (context.Arguments.Count > 0 &&
            string.Equals(context.Arguments[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            return ReplCommandResult.Continue(FormatProviderList(
                providers,
                context.Session.ProviderProfile,
                context.Session.ActiveProviderName));
        }

        if (providers.Count == 0)
        {
            return ReplCommandResult.Continue(
                "No saved providers are configured. Use /onboard to add one.",
                ReplFeedbackKind.Warning);
        }

        SavedProviderConfiguration provider;
        if (context.Arguments.Count == 0)
        {
            try
            {
                provider = await PromptForProviderAsync(
                    providers,
                    context.Session.ProviderProfile,
                    context.Session.ActiveProviderName,
                    cancellationToken);
            }
            catch (PromptCancelledException)
            {
                return ReplCommandResult.Continue(
                    "Provider switch cancelled.",
                    ReplFeedbackKind.Warning);
            }
        }
        else
        {
            string providerName = context.ArgumentText.Trim();
            provider = providers.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, providerName, StringComparison.OrdinalIgnoreCase))
                ?? providers.FirstOrDefault(candidate =>
                    candidate.Name.StartsWith(providerName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Provider '{providerName}' is not configured.");
        }

        return await SwitchProviderAsync(provider, context, cancellationToken);
    }

    private async Task<SavedProviderConfiguration> PromptForProviderAsync(
        IReadOnlyList<SavedProviderConfiguration> providers,
        AgentProviderProfile activeProviderProfile,
        string? activeProviderName,
        CancellationToken cancellationToken)
    {
        return await _selectionPrompt.PromptAsync(
            new SelectionPromptRequest<SavedProviderConfiguration>(
                "Choose provider",
                providers
                    .Select(provider =>
                    {
                        bool isActive = IsActiveProvider(
                            provider,
                            activeProviderProfile,
                            activeProviderName);
                        return new SelectionPromptOption<SavedProviderConfiguration>(
                            provider.Name,
                            provider,
                            isActive
                                ? "Currently active."
                                : $"{provider.ProviderProfile.ProviderKind.ToDisplayName()} - switch to this provider.");
                    })
                    .ToArray(),
                "Use /onboard to add another provider. Esc cancels.",
                DefaultIndex: GetActiveProviderIndex(providers, activeProviderProfile, activeProviderName),
                AllowCancellation: true),
            cancellationToken);
    }

    private async Task<ReplCommandResult> SwitchProviderAsync(
        SavedProviderConfiguration provider,
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        string? providerSecret = await _secretStore.LoadAsync(provider.Name, cancellationToken);
        if (string.IsNullOrWhiteSpace(providerSecret))
        {
            return ReplCommandResult.Continue(
                $"Provider '{provider.Name}' is missing credentials. Run /onboard to configure it again.",
                ReplFeedbackKind.Error);
        }

        await _secretStore.SaveAsync(providerSecret, cancellationToken);
        await _configurationStore.SetActiveProviderAsync(provider.Name, cancellationToken);

        ModelDiscoveryResult modelResult;
        try
        {
            modelResult = await _modelDiscoveryService.DiscoverAndSelectAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is ModelDiscoveryException or ModelProviderException or HttpRequestException or InvalidOperationException)
        {
            return ReplCommandResult.Continue(
                $"Provider '{provider.Name}' is selected, but validation failed: {exception.Message}",
                ReplFeedbackKind.Error);
        }

        context.Session.ReplaceProviderConfiguration(
            provider.ProviderProfile,
            modelResult.SelectedModelId,
            modelResult.AvailableModels.Select(static model => model.Id).ToArray(),
            CreateModelContextWindowMap(modelResult.AvailableModels),
            provider.Name);

        await _configurationStore.SaveAsync(
            new AgentConfiguration(
                context.Session.ProviderProfile,
                context.Session.ActiveModelId,
                context.Session.ReasoningEffort,
                provider.Name),
            cancellationToken);

        return ReplCommandResult.Continue(
            $"Switched provider to '{provider.Name}'.\n" +
            $"Provider: {context.Session.ProviderName}\n" +
            $"Active model: {context.Session.ActiveModelId}\n" +
            $"Available models: {context.Session.AvailableModelIds.Count}");
    }

    private static string FormatProviderList(
        IReadOnlyList<SavedProviderConfiguration> providers,
        AgentProviderProfile activeProviderProfile,
        string? activeProviderName)
    {
        if (providers.Count == 0)
        {
            return "No saved providers are configured. Use /onboard to add one.";
        }

        return "Saved providers:\n" + string.Join(
            "\n",
            providers.Select(provider =>
            {
                string activeMarker = IsActiveProvider(provider, activeProviderProfile, activeProviderName)
                    ? "* "
                    : "  ";
                string model = string.IsNullOrWhiteSpace(provider.PreferredModelId)
                    ? "no default model"
                    : provider.PreferredModelId;
                return $"{activeMarker}{provider.Name} - {provider.ProviderProfile.ProviderKind.ToDisplayName()} ({model})";
            }));
    }

    private static int GetActiveProviderIndex(
        IReadOnlyList<SavedProviderConfiguration> providers,
        AgentProviderProfile activeProviderProfile,
        string? activeProviderName)
    {
        for (int index = 0; index < providers.Count; index++)
        {
            if (IsActiveProvider(providers[index], activeProviderProfile, activeProviderName))
            {
                return index;
            }
        }

        return 0;
    }

    private static bool IsActiveProvider(
        SavedProviderConfiguration provider,
        AgentProviderProfile activeProviderProfile,
        string? activeProviderName)
    {
        return !string.IsNullOrWhiteSpace(activeProviderName)
            ? string.Equals(provider.Name, activeProviderName, StringComparison.OrdinalIgnoreCase)
            : Equals(provider.ProviderProfile, activeProviderProfile);
    }

    private static IReadOnlyDictionary<string, int> CreateModelContextWindowMap(
        IEnumerable<AvailableModel> models)
    {
        Dictionary<string, int> contextWindowTokens = new(StringComparer.Ordinal);
        foreach (AvailableModel model in models)
        {
            if (string.IsNullOrWhiteSpace(model.Id) ||
                model.ContextWindowTokens is not > 0)
            {
                continue;
            }

            contextWindowTokens[model.Id.Trim()] = model.ContextWindowTokens.Value;
        }

        return contextWindowTokens;
    }
}
