using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Models;

public sealed record CreateSessionRequest(
    AgentProviderProfile ProviderProfile,
    string ActiveModelId,
    IReadOnlyList<string> AvailableModelIds,
    string? ProfileName = null,
    string? ReasoningEffort = null,
    string? ThinkingMode = null,
    IReadOnlyDictionary<string, int>? ModelContextWindowTokens = null,
    IReadOnlyDictionary<string, ModelContextMetadata>? ModelContextMetadata = null,
    string? ActiveProviderName = null);
