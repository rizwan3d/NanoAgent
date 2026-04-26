using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Domain.Services;

namespace NanoAgent.Infrastructure.Storage;

internal sealed class JsonAgentConfigurationStore : IAgentConfigurationStore
{
    private readonly IUserDataPathProvider _pathProvider;

    public JsonAgentConfigurationStore(IUserDataPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public async Task<AgentConfiguration?> LoadAsync(CancellationToken cancellationToken)
    {
        string filePath = _pathProvider.GetConfigurationFilePath();
        if (!File.Exists(filePath))
        {
            return null;
        }

        string json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        AgentConfiguration? configuration = TryDeserializeConfiguration(json);
        if (configuration is not null)
        {
            return configuration;
        }

        return TryDeserializeLegacyConfiguration(json);
    }

    public async Task SaveAsync(AgentConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        AgentProfileConfigurationDocument document =
            await AgentProfileConfigurationReader.LoadUserDocumentAsync(
                _pathProvider,
                cancellationToken) ??
            new AgentProfileConfigurationDocument();

        document.ProviderProfile = configuration.ProviderProfile;
        document.PreferredModelId = configuration.PreferredModelId;
        document.ReasoningEffort = configuration.ReasoningEffort;

        await AgentProfileConfigurationReader.SaveUserDocumentAsync(
            _pathProvider,
            document,
            cancellationToken);
    }

    private static AgentConfiguration? TryDeserializeConfiguration(string json)
    {
        try
        {
            AgentProfileConfigurationDocument? document = JsonSerializer.Deserialize(
                json,
                OnboardingStorageJsonContext.Default.AgentProfileConfigurationDocument);

            if (document?.ProviderProfile is null)
            {
                return null;
            }

            AgentProviderProfile? normalizedProfile = NormalizeProfile(document.ProviderProfile);
            if (normalizedProfile is null)
            {
                return null;
            }

            return new AgentConfiguration(
                normalizedProfile,
                ModelIdMatcher.NormalizeOrNull(document.PreferredModelId),
                ReasoningEffortOptions.NormalizeOrNull(document.ReasoningEffort));
        }
        catch (JsonException)
        {
            try
            {
                AgentConfiguration? configuration = JsonSerializer.Deserialize(
                    json,
                    OnboardingStorageJsonContext.Default.AgentConfiguration);

                if (configuration is null)
                {
                    return null;
                }

                AgentProviderProfile? normalizedProfile = NormalizeProfile(configuration.ProviderProfile);
                if (normalizedProfile is null)
                {
                    return null;
                }

                return new AgentConfiguration(
                    normalizedProfile,
                    ModelIdMatcher.NormalizeOrNull(configuration.PreferredModelId),
                    ReasoningEffortOptions.NormalizeOrNull(configuration.ReasoningEffort));
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static AgentConfiguration? TryDeserializeLegacyConfiguration(string json)
    {
        try
        {
            AgentProviderProfile? legacyProfile = JsonSerializer.Deserialize(
                json,
                OnboardingStorageJsonContext.Default.AgentProviderProfile);

            AgentProviderProfile? normalizedLegacyProfile = NormalizeProfile(legacyProfile);
            return normalizedLegacyProfile is null
                ? null
                : new AgentConfiguration(normalizedLegacyProfile, null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgentProviderProfile? NormalizeProfile(AgentProviderProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        try
        {
            return profile.ProviderKind switch
            {
                ProviderKind.OpenAi => new AgentProviderProfile(ProviderKind.OpenAi, BaseUrl: null),
                ProviderKind.OpenAiChatGptAccount => new AgentProviderProfile(
                    ProviderKind.OpenAiChatGptAccount,
                    BaseUrl: null),
                ProviderKind.OpenRouter => new AgentProviderProfile(ProviderKind.OpenRouter, BaseUrl: null),
                ProviderKind.GoogleAiStudio => new AgentProviderProfile(ProviderKind.GoogleAiStudio, BaseUrl: null),
                ProviderKind.Anthropic => new AgentProviderProfile(ProviderKind.Anthropic, BaseUrl: null),
                ProviderKind.OpenAiCompatible when !string.IsNullOrWhiteSpace(profile.BaseUrl)
                    => new AgentProviderProfile(
                        ProviderKind.OpenAiCompatible,
                        CompatibleProviderBaseUrlNormalizer.Normalize(profile.BaseUrl)),
                _ => null
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
