using NanoAgent.Application.Backend;

namespace NanoAgent.Sdk;

/// <summary>
/// Read-only snapshot of an initialized agent session, projected from the
/// internal backend session state for SDK consumers.
/// </summary>
public sealed record NanoAgentSession(
    string SessionId,
    string ProviderName,
    string ModelId,
    string AgentProfileName,
    string ThinkingMode,
    string? ReasoningEffort,
    bool ShowThinking,
    string SectionTitle,
    bool IsResumedSection,
    IReadOnlyList<string> AvailableModelIds,
    IReadOnlyList<BackendConversationMessage> ConversationHistory)
{
    internal static NanoAgentSession FromBackend(BackendSessionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        return new NanoAgentSession(
            info.SessionId,
            info.ProviderName,
            info.ModelId,
            info.AgentProfileName,
            info.ThinkingMode,
            info.ReasoningEffort,
            info.ShowThinking,
            info.SectionTitle,
            info.IsResumedSection,
            info.AvailableModelIds,
            info.ConversationHistory);
    }
}
