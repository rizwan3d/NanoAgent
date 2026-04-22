using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Models;

public sealed record ConversationProviderRequest(
    AgentProviderProfile ProviderProfile,
    string ApiKey,
    string ModelId,
    IReadOnlyList<ConversationRequestMessage> Messages,
    string? SystemPrompt,
    IReadOnlyList<ToolDefinition> AvailableTools,
    string? ReasoningEffort = null);
