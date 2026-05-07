using NanoAgent.Domain.Abstractions;
using NanoAgent.Domain.Models;

namespace NanoAgent.Domain.Services;

internal sealed class AgentProviderProfileFactory : IAgentProviderProfileFactory
{
    public AgentProviderProfile CreateOpenAi()
    {
        return new AgentProviderProfile(ProviderKind.OpenAi, BaseUrl: null);
    }

    public AgentProviderProfile CreateOpenAiChatGptAccount()
    {
        return new AgentProviderProfile(ProviderKind.OpenAiChatGptAccount, BaseUrl: null);
    }

    public AgentProviderProfile CreateAnthropicClaudeAccount()
    {
        return new AgentProviderProfile(ProviderKind.AnthropicClaudeAccount, BaseUrl: null);
    }

    public AgentProviderProfile CreateGitHubCopilot()
    {
        return new AgentProviderProfile(ProviderKind.GitHubCopilot, BaseUrl: null);
    }

    public AgentProviderProfile CreateOpenRouter()
    {
        return new AgentProviderProfile(ProviderKind.OpenRouter, BaseUrl: null);
    }

    public AgentProviderProfile CreateKiloCode()
    {
        return new AgentProviderProfile(ProviderKind.KiloCode, BaseUrl: null);
    }

    public AgentProviderProfile CreateGoogleAntigravity()
    {
        return new AgentProviderProfile(ProviderKind.GoogleAntigravity, BaseUrl: null);
    }

    public AgentProviderProfile CreateOllama()
    {
        return new AgentProviderProfile(ProviderKind.Ollama, BaseUrl: null);
    }

    public AgentProviderProfile CreateOllamaCloud()
    {
        return new AgentProviderProfile(ProviderKind.OllamaCloud, BaseUrl: null);
    }

    public AgentProviderProfile CreateCerebras()
    {
        return new AgentProviderProfile(ProviderKind.Cerebras, BaseUrl: null);
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
