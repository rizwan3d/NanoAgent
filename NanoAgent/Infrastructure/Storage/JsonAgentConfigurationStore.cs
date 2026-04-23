using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

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

        AgentProviderProfile? legacyProfile = JsonSerializer.Deserialize(
            json,
            OnboardingStorageJsonContext.Default.AgentProviderProfile);

        AgentProviderProfile? normalizedLegacyProfile = NormalizeProfile(legacyProfile);
        return normalizedLegacyProfile is null
            ? null
            : new AgentConfiguration(normalizedLegacyProfile, null);
    }

    public async Task SaveAsync(AgentConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string filePath = _pathProvider.GetConfigurationFilePath();
        string directoryPath = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Configuration path does not contain a parent directory.");

        FilePermissionHelper.EnsurePrivateDirectory(directoryPath);

        await using FileStream stream = new(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await JsonSerializer.SerializeAsync(
            stream,
            configuration,
            OnboardingStorageJsonContext.Default.AgentConfiguration,
            cancellationToken);

        await stream.FlushAsync(cancellationToken);
        FilePermissionHelper.EnsurePrivateFile(filePath);
    }

    private static AgentConfiguration? TryDeserializeConfiguration(string json)
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
                NormalizeModelId(configuration.PreferredModelId),
                ReasoningEffortOptions.NormalizeOrNull(configuration.ReasoningEffort));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeModelId(string? modelId)
    {
        string normalizedModelId = modelId?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedModelId)
            ? null
            : normalizedModelId;
    }

    private static AgentProviderProfile? NormalizeProfile(AgentProviderProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return profile.ProviderKind switch
        {
            ProviderKind.OpenAi => new AgentProviderProfile(ProviderKind.OpenAi, BaseUrl: null),
            ProviderKind.GoogleAiStudio => new AgentProviderProfile(ProviderKind.GoogleAiStudio, BaseUrl: null),
            ProviderKind.Anthropic => new AgentProviderProfile(ProviderKind.Anthropic, BaseUrl: null),
            ProviderKind.OpenAiCompatible when !string.IsNullOrWhiteSpace(profile.BaseUrl)
                => new AgentProviderProfile(
                    ProviderKind.OpenAiCompatible,
                    profile.BaseUrl.Trim().TrimEnd('/')),
            _ => null
        };
    }
}
