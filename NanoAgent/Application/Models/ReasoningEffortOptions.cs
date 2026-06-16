namespace NanoAgent.Application.Models;

public static class ReasoningEffortOptions
{
    public const string None = "none";
    public const string Minimal = "minimal";
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string XHigh = "xhigh";
    public const string Max = "max";

    private static readonly string[] Values =
    [
        None,
        Minimal,
        Low,
        Medium,
        High,
        XHigh,
        Max
    ];

    public static IReadOnlyList<string> SupportedValues => Values;

    public static string SupportedValuesText => string.Join(", ", Values);

    public static string? NormalizeOrNull(string? reasoningEffort)
    {
        return NormalizeCore(reasoningEffort);
    }

    public static string? NormalizeOrThrow(string? reasoningEffort)
    {
        string normalized = NormalizeInput(reasoningEffort);
        if (normalized.Length == 0)
        {
            return null;
        }

        string? normalizedMode = NormalizeCore(normalized);
        if (normalizedMode is not null)
        {
            return normalizedMode;
        }

        throw new ArgumentException(
            $"Unsupported reasoning effort '{reasoningEffort?.Trim()}'. Supported values: {SupportedValuesText}.",
            nameof(reasoningEffort));
    }

    public static string Format(string? reasoningEffort)
    {
        return NormalizeOrNull(reasoningEffort) ?? "(provider default)";
    }

    public static string? TryNormalizeLegacyThinkingMode(string? value)
    {
        string normalized = NormalizeInput(value);
        return normalized switch
        {
            ThinkingModeOptions.On => ThinkingModeOptions.On,
            ThinkingModeOptions.Off => ThinkingModeOptions.Off,
            _ => null
        };
    }

    private static string NormalizeInput(string? reasoningEffort)
    {
        return string.IsNullOrWhiteSpace(reasoningEffort)
            ? string.Empty
            : reasoningEffort.Trim().ToLowerInvariant();
    }

    private static string? NormalizeCore(string? reasoningEffort)
    {
        string normalized = NormalizeInput(reasoningEffort);
        return normalized switch
        {
            "" => null,
            None => None,
            Minimal => Minimal,
            Low => Low,
            Medium => Medium,
            High => High,
            XHigh => XHigh,
            Max => Max,
            _ => null
        };
    }
}
