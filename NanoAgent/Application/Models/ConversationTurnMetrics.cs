namespace NanoAgent.Application.Models;

public sealed class ConversationTurnMetrics
{
    public ConversationTurnMetrics(
        TimeSpan elapsed,
        int estimatedOutputTokens,
        int? sessionEstimatedOutputTokens = null,
        int estimatedInputTokens = 0,
        int providerRetryCount = 0,
        int toolRoundCount = 0)
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

        if (toolRoundCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toolRoundCount));
        }

        Elapsed = elapsed;
        EstimatedOutputTokens = estimatedOutputTokens;
        SessionEstimatedOutputTokens = sessionEstimatedOutputTokens;
        EstimatedInputTokens = estimatedInputTokens;
        ProviderRetryCount = providerRetryCount;
        ToolRoundCount = toolRoundCount;
    }

    public TimeSpan Elapsed { get; }

    public int EstimatedInputTokens { get; }

    public int EstimatedOutputTokens { get; }

    public int EstimatedTotalTokens => EstimatedInputTokens + EstimatedOutputTokens;

    public int ProviderRetryCount { get; }

    public int? SessionEstimatedOutputTokens { get; }

    public int ToolRoundCount { get; }

    public int DisplayedEstimatedOutputTokens => SessionEstimatedOutputTokens ?? EstimatedOutputTokens;

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
            ProviderRetryCount,
            ToolRoundCount);
    }
}
