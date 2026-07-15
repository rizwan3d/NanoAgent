namespace NanoAgent.Application.Models;

public sealed class ConversationTurnMetrics
{
    public ConversationTurnMetrics(
        TimeSpan elapsed,
        int estimatedOutputTokens,
        int? sessionEstimatedOutputTokens = null,
        int estimatedInputTokens = 0,
        int cachedInputTokens = 0,
        int providerRetryCount = 0,
        int toolRoundCount = 0,
        string? providerName = null,
        string? modelId = null)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        if (estimatedOutputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedOutputTokens));
        }

        if (sessionEstimatedOutputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionEstimatedOutputTokens));
        }

        if (estimatedInputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedInputTokens));
        }

        if (providerRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(providerRetryCount));
        }

        if (cachedInputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cachedInputTokens));
        }

        if (toolRoundCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toolRoundCount));
        }

        Elapsed = elapsed;
        EstimatedOutputTokens = estimatedOutputTokens;
        SessionEstimatedOutputTokens = sessionEstimatedOutputTokens;
        EstimatedInputTokens = estimatedInputTokens;
        CachedInputTokens = cachedInputTokens;
        ProviderRetryCount = providerRetryCount;
        ToolRoundCount = toolRoundCount;
        ProviderName = string.IsNullOrWhiteSpace(providerName)
            ? null
            : providerName.Trim();
        ModelId = string.IsNullOrWhiteSpace(modelId)
            ? null
            : modelId.Trim();
    }

    public int CachedInputTokens { get; }

    public TimeSpan Elapsed { get; }

    public int EstimatedInputTokens { get; }

    public int EstimatedOutputTokens { get; }

    public int EstimatedTotalTokens => EstimatedInputTokens + EstimatedOutputTokens;

    public int ProviderRetryCount { get; }

    public int? SessionEstimatedOutputTokens { get; }

    public int ToolRoundCount { get; }

    public int DisplayedEstimatedOutputTokens => SessionEstimatedOutputTokens ?? EstimatedOutputTokens;

    public string? ModelId { get; }

    public string? ProviderName { get; }

    public string ToDisplayText()
    {
        return MetricDisplayFormatter.FormatEstimatedOutputMetric(
            Elapsed,
            DisplayedEstimatedOutputTokens);
    }

    public ConversationTurnMetrics WithSessionEstimatedOutputTokens(int sessionEstimatedOutputTokens)
    {
        return new ConversationTurnMetrics(
            Elapsed,
            EstimatedOutputTokens,
            sessionEstimatedOutputTokens,
            EstimatedInputTokens,
            CachedInputTokens,
            ProviderRetryCount,
            ToolRoundCount,
            ProviderName,
            ModelId);
    }
}
