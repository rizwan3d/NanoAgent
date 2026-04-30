using NanoAgent.Application.Tools;
using System.Security.Cryptography;
using System.Text;

namespace NanoAgent.Infrastructure.CustomTools;

internal static class CustomToolName
{
    private const int MaxToolNameLength = 64;

    public static string Create(
        string configuredName,
        IReadOnlySet<string> usedToolNames)
    {
        string sanitizedName = SanitizeSegment(configuredName);
        string candidate = $"{AgentToolNames.CustomToolPrefix}{sanitizedName}";

        if (candidate.Length <= MaxToolNameLength && !usedToolNames.Contains(candidate))
        {
            return candidate;
        }

        string suffix = "__" + CreateShortHash(configuredName);
        int remaining = MaxToolNameLength - AgentToolNames.CustomToolPrefix.Length - suffix.Length;
        candidate =
            AgentToolNames.CustomToolPrefix +
            sanitizedName[..Math.Min(sanitizedName.Length, Math.Max(1, remaining))] +
            suffix;

        if (!usedToolNames.Contains(candidate))
        {
            return candidate;
        }

        for (int index = 2; index < 100; index++)
        {
            string indexedSuffix = $"{suffix}_{index}";
            int indexedRemaining = MaxToolNameLength - AgentToolNames.CustomToolPrefix.Length - indexedSuffix.Length;
            string indexedCandidate =
                AgentToolNames.CustomToolPrefix +
                sanitizedName[..Math.Min(sanitizedName.Length, Math.Max(1, indexedRemaining))] +
                indexedSuffix;
            if (!usedToolNames.Contains(indexedCandidate))
            {
                return indexedCandidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not create a unique custom tool name for '{configuredName}'.");
    }

    private static string SanitizeSegment(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_');
            }
        }

        string sanitized = builder.ToString().Trim('_');
        return sanitized.Length == 0
            ? "tool"
            : sanitized;
    }

    private static string CreateShortHash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }
}
