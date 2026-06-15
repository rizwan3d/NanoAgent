using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Models;

public sealed record ConversationProviderRequest(
    AgentProviderProfile ProviderProfile,
    string ApiKey,
    string ModelId,
    IReadOnlyList<ConversationRequestMessage> Messages,
    string? SystemPrompt,
    IReadOnlyList<ToolDefinition> AvailableTools,
    string? ReasoningEffort = null,
    Func<string, CancellationToken, Task>? OnAssistantMessageChunkAsync = null,
    // Invoked by the transport before each retry back-off so the UI can surface
    // attempt progress. Optional: null when nobody is listening for retries.
    Func<ProviderRetryProgress, CancellationToken, Task>? OnProviderRetryAsync = null);
