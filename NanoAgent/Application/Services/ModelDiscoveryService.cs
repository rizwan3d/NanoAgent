using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Logging;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Abstractions;
using NanoAgent.Domain.Models;
using NanoAgent.Domain.Services;
using System.Security.Cryptography;
using System.Text;

namespace NanoAgent.Application.Services;

internal sealed class ModelDiscoveryService : IModelDiscoveryService
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IApiKeySecretStore _secretStore;
    private readonly IModelProviderClient _modelProviderClient;
    private readonly IModelCache _modelCache;
    private readonly IModelSelectionPolicy _modelSelectionPolicy;
    private readonly ModelSelectionSettings _settings;
    private readonly ILogger<ModelDiscoveryService> _logger;

    public ModelDiscoveryService(
        IAgentConfigurationStore configurationStore,
        IApiKeySecretStore secretStore,
        IModelProviderClient modelProviderClient,
        IModelCache modelCache,
        IModelSelectionPolicy modelSelectionPolicy,
        ModelSelectionSettings settings,
        ILogger<ModelDiscoveryService> logger)
    {
        _configurationStore = configurationStore;
        _secretStore = secretStore;
        _modelProviderClient = modelProviderClient;
        _modelCache = modelCache;
        _modelSelectionPolicy = modelSelectionPolicy;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ModelDiscoveryResult> DiscoverAndSelectAsync(CancellationToken cancellationToken)
    {
        AgentConfiguration configuration = await _configurationStore.LoadAsync(cancellationToken)
            ?? throw new ModelDiscoveryException(
                "Model discovery cannot start because provider configuration is missing.");
        AgentProviderProfile providerProfile = configuration.ProviderProfile;

        string apiKey = await LoadProviderSecretAsync(configuration, cancellationToken)
            ?? throw new ModelDiscoveryException(
                "Model discovery cannot start because the API key is missing.");

        ApplicationLogMessages.ModelDiscoveryStarted(
            _logger,
            providerProfile.ProviderKind.ToDisplayName());

        (IReadOnlyList<AvailableModel> models, bool hadDuplicates) = await LoadAvailableModelsAsync(
            providerProfile,
            apiKey,
            _settings.CacheDuration,
            cancellationToken);

        if (hadDuplicates)
        {
            ApplicationLogMessages.DuplicateModelsDetected(_logger);
        }

        ModelSelectionDecision selection = _modelSelectionPolicy.Select(
            new ModelSelectionContext(
                models,
                configuration.PreferredModelId));

        if (selection.ConfiguredDefaultStatus == ConfiguredDefaultModelStatus.NotFound &&
            selection.ConfiguredDefaultModel is not null)
        {
            ApplicationLogMessages.ConfiguredDefaultModelNotFound(
                _logger,
                selection.ConfiguredDefaultModel);
        }

        if (!string.Equals(
                ModelIdMatcher.NormalizeOrNull(configuration.PreferredModelId),
                selection.SelectedModelId,
                StringComparison.Ordinal))
        {
            await _configurationStore.SaveAsync(
                new AgentConfiguration(
                    providerProfile,
                    selection.SelectedModelId,
                    configuration.ReasoningEffort),
                cancellationToken);
        }

        return new ModelDiscoveryResult(
            models,
            selection.SelectedModelId,
            selection.SelectionSource,
            selection.ConfiguredDefaultStatus,
            selection.ConfiguredDefaultModel,
            hadDuplicates);
    }

    private async Task<string?> LoadProviderSecretAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuration.ActiveProviderName))
        {
            string? providerSecret = await _secretStore.LoadAsync(
                configuration.ActiveProviderName,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(providerSecret))
            {
                return providerSecret;
            }
        }

        return await _secretStore.LoadAsync(cancellationToken);
    }

    private async Task<(IReadOnlyList<AvailableModel> Models, bool HadDuplicates)> LoadAvailableModelsAsync(
        AgentProviderProfile providerProfile,
        string apiKey,
        TimeSpan cacheDuration,
        CancellationToken cancellationToken)
    {
        string cacheKey = BuildCacheKey(providerProfile, apiKey);
        IReadOnlyList<AvailableModel> cachedModels = await _modelCache.GetOrCreateAsync(
            cacheKey,
            cacheDuration,
            fetchToken => _modelProviderClient.GetAvailableModelsAsync(
                providerProfile,
                apiKey,
                fetchToken),
            cancellationToken);

        bool hadDuplicates = false;
        List<AvailableModel> normalizedModels = [];
        HashSet<string> uniqueIds = new(StringComparer.Ordinal);

        foreach (AvailableModel model in cachedModels
                     .Where(static model => !string.IsNullOrWhiteSpace(model.Id)))
        {
            string normalizedId = model.Id.Trim();
            if (!uniqueIds.Add(normalizedId))
            {
                hadDuplicates = true;
                continue;
            }

            normalizedModels.Add(new AvailableModel(
                normalizedId,
                NormalizeContextWindowTokens(model.ContextWindowTokens)));
        }

        if (normalizedModels.Count == 0)
        {
            throw new ModelDiscoveryException(
                "The configured provider returned no usable models.");
        }

        return (normalizedModels, hadDuplicates);
    }

    private static string BuildCacheKey(AgentProviderProfile providerProfile, string apiKey)
    {
        string rawKey = $"{providerProfile.ProviderKind}|{providerProfile.ResolveBaseUrl()}|{apiKey}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(hashBytes);
    }

    private static int? NormalizeContextWindowTokens(int? contextWindowTokens)
    {
        return contextWindowTokens is > 0
            ? contextWindowTokens
            : null;
    }
}
