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
    IReadOnlyList<BackendConversationMessage> ConversationHistory);

public sealed record BackendConversationMessage(
    string Role,
    string Content);
