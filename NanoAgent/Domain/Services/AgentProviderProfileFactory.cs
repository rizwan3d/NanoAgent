using NanoAgent.Domain.Abstractions;
using NanoAgent.Domain.Models;

namespace NanoAgent.Domain.Services;

internal sealed class AgentProviderProfileFactory : IAgentProviderProfileFactory
{
    public AgentProviderProfile CreateOpenAi()
    {
        return new AgentProviderProfile(ProviderKind.OpenAi, BaseUrl: null);
    }

    public AgentProviderProfile CreateGoogleAiStudio()
    {
        return new AgentProviderProfile(ProviderKind.GoogleAiStudio, BaseUrl: null);
    }

    public AgentProviderProfile CreateAnthropic()
    {
        return new AgentProviderProfile(ProviderKind.Anthropic, BaseUrl: null);
    }

    public AgentProviderProfile CreateCompatible(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        string normalizedBaseUrl = CompatibleProviderBaseUrlNormalizer.Normalize(baseUrl);
        return new AgentProviderProfile(ProviderKind.OpenAiCompatible, normalizedBaseUrl);
    }
}
