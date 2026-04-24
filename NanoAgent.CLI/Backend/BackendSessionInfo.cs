namespace NanoAgent.CLI;

public sealed record BackendSessionInfo(
    string SessionId,
    string SectionResumeCommand,
    string ProviderName,
    string ModelId,
    IReadOnlyList<string> AvailableModelIds,
    string ThinkingMode,
    string AgentProfileName,
    string SectionTitle,
    bool IsResumedSection,
    IReadOnlyList<BackendConversationMessage> ConversationHistory);

public sealed record BackendConversationMessage(
    string Role,
    string Content);
