using System.Text;

namespace NanoAgent.Application.Utilities;

public static class ModelDisplayFormatter
{
    public static string ToDisplayName(this string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return modelId ?? string.Empty;

        return FormatModelName(modelId);
    }

    public static string ToDisplayNameWithProvider(this string modelId, string? providerName)
    {
        string formatted = modelId.ToDisplayName();
        if (string.IsNullOrWhiteSpace(providerName))
            return formatted;
        return $"{formatted} ({providerName})";
    }
    

    private static string FormatModelName(string modelId)
    {
        string[] parts = modelId.Split('-');
        StringBuilder result = new();

        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                result.Append(' ');

            string part = parts[i];
            if (part.Length == 0)
                continue;

            result.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                result.Append(part.AsSpan(1));
        }

        return result.ToString();
    }
}
