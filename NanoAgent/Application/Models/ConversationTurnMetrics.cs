namespace NanoAgent.Application.Models;

public sealed class ConversationTurnMetrics
{
    public ConversationTurnMetrics(
        TimeSpan elapsed,
        int estimatedOutputTokens,
        int? sessionEstimatedOutputTokens = null)
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

        Elapsed = elapsed;
        EstimatedOutputTokens = estimatedOutputTokens;
        SessionEstimatedOutputTokens = sessionEstimatedOutputTokens;
    }

    public TimeSpan Elapsed { get; }

    public int EstimatedOutputTokens { get; }

    public int? SessionEstimatedOutputTokens { get; }

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
            sessionEstimatedOutputTokens);
    }
}
