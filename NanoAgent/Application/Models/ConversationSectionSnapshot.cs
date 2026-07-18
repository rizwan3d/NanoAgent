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
        string? thinkingMode = null,
        SessionStateSnapshot? sessionState = null,
        string? workspacePath = null,
        IReadOnlyDictionary<string, int>? modelContextWindowTokens = null,
        IReadOnlyDictionary<string, ModelContextMetadata>? modelContextMetadata = null,
        string? activeProviderName = null,
        string? parentSessionId = null)
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
        ActiveProviderName = NormalizeProviderName(activeProviderName);
        AgentProfileName = string.IsNullOrWhiteSpace(agentProfileName)
            ? BuiltInAgentProfiles.BuildName
            : agentProfileName.Trim();
        AvailableModelIds = normalizedAvailableModelIds;
        ModelContextMetadata = NormalizeModelContextMetadata(
            modelContextMetadata,
            modelContextWindowTokens,
            AvailableModelIds);
        ModelContextWindowTokens = CreateModelContextWindowTokens(ModelContextMetadata);
        CreatedAtUtc = createdAtUtc;
        ProviderProfile = providerProfile;
        ReasoningOptions normalizedReasoning = ReasoningOptions.Create(
            thinkingMode,
            reasoningEffort);
        ReasoningEffort = normalizedReasoning.ReasoningEffort;
        ThinkingMode = normalizedReasoning.ThinkingMode;
        SectionId = sectionId.Trim();
        Title = title.Trim();
        TotalEstimatedOutputTokens = totalEstimatedOutputTokens;
        Turns = turns
            .Where(static turn => turn is not null)
            .Select(static turn => NormalizeLegacyTurnStatus(turn))
            .ToArray();
        UpdatedAtUtc = updatedAtUtc;
        PendingExecutionPlan = pendingExecutionPlan;
        SessionState = sessionState ?? SessionStateSnapshot.Empty;
        WorkspacePath = string.IsNullOrWhiteSpace(workspacePath)
            ? null
            : Path.GetFullPath(workspacePath.Trim());
        ParentSessionId = NormalizeOptionalSessionId(parentSessionId);
    }

    public string ActiveModelId { get; }

    public string? ActiveProviderName { get; }

    public string AgentProfileName { get; }

    public IReadOnlyList<string> AvailableModelIds { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyDictionary<string, int> ModelContextWindowTokens { get; }

    public IReadOnlyDictionary<string, ModelContextMetadata> ModelContextMetadata { get; }

    public AgentProviderProfile ProviderProfile { get; }

    public PendingExecutionPlan? PendingExecutionPlan { get; }

    public string? ReasoningEffort { get; }

    public string ThinkingMode { get; }

    public string SectionId { get; }

    public SessionStateSnapshot SessionState { get; }

    public string Title { get; }

    public int TotalEstimatedOutputTokens { get; }

    public IReadOnlyList<ConversationSectionTurn> Turns { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public string? WorkspacePath { get; }

    /// <summary>
    /// The ID of the parent session that contains this section.
    /// Null when the section is standalone (legacy format).
    /// </summary>
    public string? ParentSessionId { get; }

    private static ConversationSectionTurn NormalizeLegacyTurnStatus(ConversationSectionTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        return turn.Status == ConversationTurnStatus.Pending &&
            !string.IsNullOrWhiteSpace(turn.AssistantResponse) &&
            turn.FailureInfo is null
                ? new ConversationSectionTurn(
                    userInput: turn.UserInput,
                    assistantResponse: turn.AssistantResponse,
                    toolCalls: turn.ToolCalls,
                    toolOutputMessages: turn.ToolOutputMessages,
                    assistantReasoningContent: turn.AssistantReasoningContent,
                    assistantReasoningDetailsJson: turn.AssistantReasoningDetailsJson,
                    status: ConversationTurnStatus.Completed,
                    turnId: turn.TurnId,
                    attachments: turn.Attachments)
                : turn;
    }

    private static Dictionary<string, ModelContextMetadata> NormalizeModelContextMetadata(
        IReadOnlyDictionary<string, ModelContextMetadata>? modelContextMetadata,
        IReadOnlyDictionary<string, int>? modelContextWindowTokens,
        IReadOnlyList<string> availableModelIds)
    {
        Dictionary<string, ModelContextMetadata> normalized = new(StringComparer.Ordinal);
        HashSet<string> available = new(availableModelIds, StringComparer.Ordinal);

        if (modelContextMetadata is not null)
        {
            foreach ((string modelId, ModelContextMetadata metadata) in modelContextMetadata)
            {
                if (string.IsNullOrWhiteSpace(modelId) ||
                    metadata is null ||
                    metadata.ContextWindowTokens <= 0)
                {
                    continue;
                }

                string normalizedModelId = modelId.Trim();
                if (available.Contains(normalizedModelId))
                {
                    normalized[normalizedModelId] = metadata;
                }
            }
        }

        if (modelContextWindowTokens is null)
        {
            return normalized;
        }

        foreach ((string modelId, int contextWindowTokens) in modelContextWindowTokens)
        {
            if (string.IsNullOrWhiteSpace(modelId) || contextWindowTokens <= 0)
            {
                continue;
            }

            string normalizedModelId = modelId.Trim();
            if (available.Contains(normalizedModelId) &&
                !normalized.ContainsKey(normalizedModelId))
            {
                normalized[normalizedModelId] = new ModelContextMetadata(contextWindowTokens);
            }
        }

        return normalized;
    }

    private static Dictionary<string, int> CreateModelContextWindowTokens(
        IReadOnlyDictionary<string, ModelContextMetadata> modelContextMetadata)
    {
        Dictionary<string, int> contextWindowTokens = new(StringComparer.Ordinal);
        foreach ((string modelId, ModelContextMetadata metadata) in modelContextMetadata)
        {
            if (metadata.ContextWindowTokens > 0)
            {
                contextWindowTokens[modelId] = metadata.ContextWindowTokens;
            }
        }

        return contextWindowTokens;
    }

    private static string? NormalizeProviderName(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return null;
        }

        string normalized = new(
            providerName
                .Trim()
                .Where(static character => !char.IsControl(character))
                .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    private static string? NormalizeOptionalSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (!Guid.TryParse(sessionId.Trim(), out Guid parsedSessionId))
        {
            throw new ArgumentException(
                "Session id must be a valid GUID.",
                nameof(sessionId));
        }

        return parsedSessionId.ToString("D");
    }
}
