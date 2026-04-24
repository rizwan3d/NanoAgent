using System.Text;

namespace NanoAgent.Infrastructure.Mcp;

internal static class McpToolName
{
    private const int MaxToolNameLength = 64;
    private const string Prefix = "mcp__";

    public static string Create(
        string serverName,
        string remoteToolName,
        IReadOnlySet<string> usedToolNames)
    {
        string sanitizedServerName = SanitizeSegment(serverName);
        string sanitizedToolName = SanitizeSegment(remoteToolName);
        string candidate = $"{Prefix}{sanitizedServerName}__{sanitizedToolName}";

        if (candidate.Length <= MaxToolNameLength && !usedToolNames.Contains(candidate))
        {
            return candidate;
        }

        string hash = McpJson.CreateShortHash($"{serverName}\n{remoteToolName}");
        string suffix = $"__{hash}";
        int remaining = MaxToolNameLength - Prefix.Length - suffix.Length - 2;
        int serverLength = Math.Min(sanitizedServerName.Length, Math.Max(8, remaining / 3));
        int toolLength = Math.Max(1, remaining - serverLength);
        candidate =
            $"{Prefix}{sanitizedServerName[..Math.Min(serverLength, sanitizedServerName.Length)]}" +
            $"__{sanitizedToolName[..Math.Min(toolLength, sanitizedToolName.Length)]}" +
            suffix;

        if (!usedToolNames.Contains(candidate))
        {
            return candidate;
        }

        for (int index = 2; index < 100; index++)
        {
            string indexedSuffix = $"{suffix}_{index}";
            int indexedRemaining = MaxToolNameLength - Prefix.Length - indexedSuffix.Length - 2;
            int indexedServerLength = Math.Min(sanitizedServerName.Length, Math.Max(4, indexedRemaining / 3));
            int indexedToolLength = Math.Max(1, indexedRemaining - indexedServerLength);
            string indexedCandidate =
                $"{Prefix}{sanitizedServerName[..Math.Min(indexedServerLength, sanitizedServerName.Length)]}" +
                $"__{sanitizedToolName[..Math.Min(indexedToolLength, sanitizedToolName.Length)]}" +
                indexedSuffix;
            if (!usedToolNames.Contains(indexedCandidate))
            {
                return indexedCandidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not create a unique MCP tool name for '{serverName}/{remoteToolName}'.");
    }

    private static string SanitizeSegment(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char c in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            {
                builder.Append(c);
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
}
