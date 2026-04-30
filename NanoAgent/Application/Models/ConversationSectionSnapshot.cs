using NanoAgent.Application.Profiles;
using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Models;

public sealed class ConversationSectionSnapshot
{
    public ConversationSectionSnapshot(
        string sectionId,
        string title,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        IReadOnlyList<ConversationSectionTurn> turns,
        int totalEstimatedOutputTokens,
        PendingExecutionPlan? pendingExecutionPlan = null,
        string? agentProfileName = null,
        string? reasoningEffort = null,
        SessionStateSnapshot? sessionState = null,
        string? workspacePath = null,
        IReadOnlyDictionary<string, int>? modelContextWindowTokens = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(providerProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeModelId);
        ArgumentNullException.ThrowIfNull(availableModelIds);
        ArgumentNullException.ThrowIfNull(turns);

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
        }

        if (totalEstimatedOutputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalEstimatedOutputTokens));
        }

        string normalizedActiveModelId = activeModelId.Trim();
        List<string> normalizedAvailableModelIds = availableModelIds
            .Where(static modelId => !string.IsNullOrWhiteSpace(modelId))
            .Select(static modelId => modelId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!normalizedAvailableModelIds.Contains(normalizedActiveModelId, StringComparer.Ordinal))
        {
            normalizedAvailableModelIds.Insert(0, normalizedActiveModelId);
        }

        ActiveModelId = normalizedActiveModelId;
        AgentProfileName = string.IsNullOrWhiteSpace(agentProfileName)
            ? BuiltInAgentProfiles.BuildName
            : agentProfileName.Trim();
        AvailableModelIds = normalizedAvailableModelIds;
        ModelContextWindowTokens = NormalizeModelContextWindowTokens(
            modelContextWindowTokens,
            AvailableModelIds);
        CreatedAtUtc = createdAtUtc;
        ProviderProfile = providerProfile;
        ReasoningEffort = ReasoningEffortOptions.NormalizeOrNull(reasoningEffort);
        SectionId = sectionId.Trim();
        Title = title.Trim();
        TotalEstimatedOutputTokens = totalEstimatedOutputTokens;
        Turns = turns
            .Where(static turn => turn is not null)
            .ToArray();
        UpdatedAtUtc = updatedAtUtc;
        PendingExecutionPlan = pendingExecutionPlan;
        SessionState = sessionState ?? SessionStateSnapshot.Empty;
        WorkspacePath = string.IsNullOrWhiteSpace(workspacePath)
            ? null
            : Path.GetFullPath(workspacePath.Trim());
    }

    public string ActiveModelId { get; }

    public string AgentProfileName { get; }

    public IReadOnlyList<string> AvailableModelIds { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyDictionary<string, int> ModelContextWindowTokens { get; }

    public AgentProviderProfile ProviderProfile { get; }

    public PendingExecutionPlan? PendingExecutionPlan { get; }

    public string? ReasoningEffort { get; }

    public string SectionId { get; }

    public SessionStateSnapshot SessionState { get; }

    public string Title { get; }

    public int TotalEstimatedOutputTokens { get; }

    public IReadOnlyList<ConversationSectionTurn> Turns { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public string? WorkspacePath { get; }

    private static Dictionary<string, int> NormalizeModelContextWindowTokens(
        IReadOnlyDictionary<string, int>? modelContextWindowTokens,
        IReadOnlyList<string> availableModelIds)
    {
        Dictionary<string, int> normalized = new(StringComparer.Ordinal);
        if (modelContextWindowTokens is null || modelContextWindowTokens.Count == 0)
        {
            return normalized;
        }

        HashSet<string> available = new(availableModelIds, StringComparer.Ordinal);
        foreach ((string modelId, int contextWindowTokens) in modelContextWindowTokens)
        {
            if (string.IsNullOrWhiteSpace(modelId) || contextWindowTokens <= 0)
            {
                continue;
            }

            string normalizedModelId = modelId.Trim();
            if (available.Contains(normalizedModelId))
            {
                normalized[normalizedModelId] = contextWindowTokens;
            }
        }

        return normalized;
    }
}
