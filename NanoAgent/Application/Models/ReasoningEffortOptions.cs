namespace NanoAgent.Application.Models;

public static class ReasoningEffortOptions
{
    public const string On = "on";
    public const string Off = "off";
    private const string ProviderEnabledValue = "high";

    private static readonly string[] Values =
    [
        On,
        Off
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
            $"Unsupported thinking mode '{reasoningEffort?.Trim()}'. Supported values: {SupportedValuesText}.",
            nameof(reasoningEffort));
    }

    public static string Format(string? reasoningEffort)
    {
        return NormalizeOrNull(reasoningEffort) ?? Off;
    }

    public static string? ToProviderValue(string? reasoningEffort)
    {
        return NormalizeOrNull(reasoningEffort) == On
            ? ProviderEnabledValue
            : null;
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
            On => On,
            Off => Off,
            _ => null
        };
    }
}
