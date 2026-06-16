namespace NanoAgent.Application.Models;

public static class ThinkingModeOptions
{
    public const string On = "on";
    public const string Off = "off";

    private static readonly string[] Values =
    [
        On,
        Off
    ];

    public static IReadOnlyList<string> SupportedValues => Values;

    public static string SupportedValuesText => string.Join(", ", Values);

    public static string? NormalizeOrNull(string? thinkingMode)
    {
        return NormalizeCore(thinkingMode);
    }

    public static string? NormalizeOrThrow(string? thinkingMode)
    {
        string normalized = NormalizeInput(thinkingMode);
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
            $"Unsupported thinking mode '{thinkingMode?.Trim()}'. Supported values: {SupportedValuesText}.",
            nameof(thinkingMode));
    }

    public static string Format(string? thinkingMode)
    {
        return NormalizeOrNull(thinkingMode) ?? Off;
    }

    private static string NormalizeInput(string? thinkingMode)
    {
        return string.IsNullOrWhiteSpace(thinkingMode)
            ? string.Empty
            : thinkingMode.Trim().ToLowerInvariant();
    }

    private static string? NormalizeCore(string? thinkingMode)
    {
        string normalized = NormalizeInput(thinkingMode);
        return normalized switch
        {
            "" => null,
            On => On,
            Off => Off,
            _ => null
        };
    }
}
