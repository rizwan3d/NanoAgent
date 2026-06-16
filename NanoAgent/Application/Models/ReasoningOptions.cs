namespace NanoAgent.Application.Models;

public sealed record ReasoningOptions(
    bool ThinkingEnabled,
    bool ShowThinking,
    string? ReasoningEffort = null,
    int? ReasoningMaxTokens = null,
    string? ReasoningSummary = null)
{
    public string ThinkingMode => ThinkingEnabled
        ? ThinkingModeOptions.On
        : ThinkingModeOptions.Off;

    public static ReasoningOptions Create(
        string? thinkingMode = null,
        string? reasoningEffort = ReasoningEffortOptions.High,
        bool? showThinking = null,
        int? reasoningMaxTokens = null,
        string? reasoningSummary = null)
    {
        string? legacyThinkingMode = ReasoningEffortOptions.TryNormalizeLegacyThinkingMode(reasoningEffort);
        string normalizedThinkingMode = ThinkingModeOptions.Format(thinkingMode ?? legacyThinkingMode);
        string? normalizedReasoningEffort = legacyThinkingMode is null
            ? ReasoningEffortOptions.NormalizeOrThrow(reasoningEffort)
            : null;
        bool thinkingEnabled = string.Equals(
            normalizedThinkingMode,
            ThinkingModeOptions.On,
            StringComparison.Ordinal);

        return new ReasoningOptions(
            thinkingEnabled,
            showThinking ?? thinkingEnabled,
            normalizedReasoningEffort,
            reasoningMaxTokens,
            string.IsNullOrWhiteSpace(reasoningSummary) ? null : reasoningSummary.Trim().ToLowerInvariant());
    }

    public static (string? ThinkingMode, string? ReasoningEffort) NormalizeStoredValues(
        string? thinkingMode,
        string? reasoningEffort)
    {
        string? legacyThinkingMode = ReasoningEffortOptions.TryNormalizeLegacyThinkingMode(reasoningEffort);
        string? normalizedThinkingMode = ThinkingModeOptions.NormalizeOrNull(thinkingMode ?? legacyThinkingMode);
        string? normalizedReasoningEffort = legacyThinkingMode is null
            ? ReasoningEffortOptions.NormalizeOrThrow(reasoningEffort)
            : null;

        return (normalizedThinkingMode, normalizedReasoningEffort);
    }

    public ReasoningOptions WithThinkingMode(string? thinkingMode)
    {
        string normalizedThinkingMode = ThinkingModeOptions.Format(thinkingMode);
        bool thinkingEnabled = string.Equals(
            normalizedThinkingMode,
            ThinkingModeOptions.On,
            StringComparison.Ordinal);

        return this with
        {
            ThinkingEnabled = thinkingEnabled,
            ShowThinking = thinkingEnabled
        };
    }

    public ReasoningOptions WithReasoningEffort(string? reasoningEffort)
    {
        return this with
        {
            ReasoningEffort = ReasoningEffortOptions.NormalizeOrThrow(reasoningEffort)
        };
    }
}
