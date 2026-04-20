using System.Globalization;

namespace NanoAgent.Application.Models;

internal static class MetricDisplayFormatter
{
    public static string FormatElapsed(TimeSpan elapsed)
    {
        int roundedSeconds = Math.Max(1, (int)Math.Round(
            elapsed.TotalSeconds,
            MidpointRounding.AwayFromZero));

        TimeSpan normalized = TimeSpan.FromSeconds(roundedSeconds);

        if (normalized.TotalHours >= 1d)
        {
            return $"{(int)normalized.TotalHours}h {normalized.Minutes}m {normalized.Seconds}s";
        }

        if (normalized.TotalMinutes >= 1d)
        {
            return $"{(int)normalized.TotalMinutes}m {normalized.Seconds}s";
        }

        return $"{normalized.Seconds}s";
    }

    public static string FormatEstimatedTokens(int estimatedTokens)
    {
        int safeValue = Math.Max(0, estimatedTokens);
        if (safeValue < 1_000)
        {
            return safeValue.ToString(CultureInfo.InvariantCulture);
        }

        double thousands = safeValue / 1_000d;
        string format = thousands >= 10d ? "0" : "0.#";

        return $"{Math.Round(thousands, thousands >= 10d ? 0 : 1, MidpointRounding.AwayFromZero).ToString(format, CultureInfo.InvariantCulture)}k";
    }

    public static string FormatEstimatedOutputMetric(
        TimeSpan elapsed,
        int estimatedTokens)
    {
        return $"({FormatElapsed(elapsed)} \u00B7 {FormatEstimatedTokens(estimatedTokens)} tokens est.)";
    }
}
