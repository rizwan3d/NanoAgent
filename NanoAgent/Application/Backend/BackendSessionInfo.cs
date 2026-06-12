namespace NanoAgent.Application.Backend;

public sealed record BackendSessionInfo(
    string SessionId,
    string SectionResumeCommand,
    string ProviderName,
    string ModelId,
    int? ActiveModelContextWindowTokens,
    IReadOnlyList<string> AvailableModelIds,
    string ThinkingMode,
    string AgentProfileName,
    string SectionTitle,
    bool IsResumedSection,
    IReadOnlyList<BackendConversationMessage> ConversationHistory)
{
    public IReadOnlyList<BackendAgentProfileInfo> AvailableAgentProfiles { get; init; } = [];

    public string? SessionContentText { get; init; }

    public int TotalEstimatedOutputTokens { get; init; }

    public int SectionEstimatedContextTokens { get; init; }
}

public sealed record BackendAgentProfileInfo(
    string Name,
    string Mode,
    string Description);

public sealed record BackendConversationMessage(
    string Role,
    string Content,
    string? ReasoningContent = null,
    string? ReasoningDetailsJson = null);
