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
        int totalEstimatedOutputTokens)
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
        AvailableModelIds = normalizedAvailableModelIds;
        CreatedAtUtc = createdAtUtc;
        ProviderProfile = providerProfile;
        SectionId = sectionId.Trim();
        Title = title.Trim();
        TotalEstimatedOutputTokens = totalEstimatedOutputTokens;
        Turns = turns
            .Where(static turn => turn is not null)
            .ToArray();
        UpdatedAtUtc = updatedAtUtc;
    }

    public string ActiveModelId { get; }

    public IReadOnlyList<string> AvailableModelIds { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public AgentProviderProfile ProviderProfile { get; }

    public string SectionId { get; }

    public string Title { get; }

    public int TotalEstimatedOutputTokens { get; }

    public IReadOnlyList<ConversationSectionTurn> Turns { get; }

    public DateTimeOffset UpdatedAtUtc { get; }
}
