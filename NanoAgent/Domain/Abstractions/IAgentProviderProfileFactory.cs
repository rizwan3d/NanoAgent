using NanoAgent.Domain.Models;

namespace NanoAgent.Domain.Abstractions;

public interface IAgentProviderProfileFactory
{
    AgentProviderProfile CreateOpenAi();

    AgentProviderProfile CreateOpenAiChatGptAccount();

    AgentProviderProfile CreateCompatible(string baseUrl);

    AgentProviderProfile CreateGoogleAiStudio();

    AgentProviderProfile CreateAnthropic();
}
