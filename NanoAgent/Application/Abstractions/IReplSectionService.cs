using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Abstractions;

public interface IReplSectionService
{
    Task<ReplSessionContext> CreateNewAsync(
        string applicationName,
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        IAgentProfile agentProfile,
        IReadOnlyDictionary<string, int>? modelContextWindowTokens,
        string? activeProviderName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new section within an existing session, accumulating context
    /// from the completed section into the session.
    /// </summary>
    Task<ReplSessionContext> CreateNewWithinSessionAsync(
        string applicationName,
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        IAgentProfile agentProfile,
        IReadOnlyDictionary<string, int>? modelContextWindowTokens,
        string? activeProviderName,
        ReplSessionContext completedSection,
        CancellationToken cancellationToken);

    void EnsureTitleGenerationStarted(
        ReplSessionContext session,
        string firstUserPrompt);

    Task<ReplSessionContext> ResumeAsync(
        string applicationName,
        string sectionId,
        IAgentProfile? profileOverride,
        CancellationToken cancellationToken);

    Task SaveIfDirtyAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);

    Task StopAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
