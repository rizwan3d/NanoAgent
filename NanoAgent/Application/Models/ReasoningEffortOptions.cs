namespace NanoAgent.Application.Models;

public static class ReasoningEffortOptions
{
    public const string None = "none";
    public const string Minimal = "minimal";
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string XHigh = "xhigh";

    private static readonly string[] Values =
    [
        None,
        Minimal,
        Low,
        Medium,
        High,
        XHigh
    ];

    public static IReadOnlyList<string> SupportedValues => Values;

    public static string SupportedValuesText => string.Join(", ", Values);

    public static string? NormalizeOrNull(string? reasoningEffort)
    {
        string normalized = NormalizeInput(reasoningEffort);
        return Values.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : null;
    }

    public static string? NormalizeOrThrow(string? reasoningEffort)
    {
        string normalized = NormalizeInput(reasoningEffort);
        if (normalized.Length == 0)
        {
            return null;
        }

        if (Values.Contains(normalized, StringComparer.Ordinal))
        {
            return normalized;
        }

        throw new ArgumentException(
            $"Unsupported thinking effort '{reasoningEffort?.Trim()}'. Supported values: {SupportedValuesText}.",
            nameof(reasoningEffort));
    }

    public static string Format(string? reasoningEffort)
    {
        return NormalizeOrNull(reasoningEffort) ?? "provider default";
    }

    private static string NormalizeInput(string? reasoningEffort)
    {
        return string.IsNullOrWhiteSpace(reasoningEffort)
            ? string.Empty
            : reasoningEffort.Trim().ToLowerInvariant();
    }
}
