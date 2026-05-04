using NanoAgent.Domain.Models;

namespace NanoAgent.Domain.Abstractions;

public interface IAgentProviderProfileFactory
{
    AgentProviderProfile CreateOpenAi();

    AgentProviderProfile CreateOpenAiChatGptAccount();

    AgentProviderProfile CreateAnthropicClaudeAccount();

    AgentProviderProfile CreateGitHubCopilot();

    AgentProviderProfile CreateOpenRouter();

    AgentProviderProfile CreateCompatible(string baseUrl);

    AgentProviderProfile CreateGoogleAiStudio();

    AgentProviderProfile CreateAnthropic();
}
