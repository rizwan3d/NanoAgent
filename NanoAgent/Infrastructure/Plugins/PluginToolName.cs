using System.Security.Cryptography;
using System.Text;
using NanoAgent.Application.Tools;

namespace NanoAgent.Infrastructure.Plugins;

internal static class PluginToolName
{
    private const int MaxToolNameLength = 64;

    public static string Create(
        string pluginName,
        string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        string sanitizedPluginName = SanitizeSegment(pluginName);
        string sanitizedToolName = SanitizeSegment(toolName);
        string candidate = $"{AgentToolNames.PluginToolPrefix}{sanitizedPluginName}__{sanitizedToolName}";

        if (candidate.Length <= MaxToolNameLength)
        {
            return candidate;
        }

        string hash = CreateShortHash($"{pluginName}\n{toolName}");
        string suffix = $"__{hash}";
        int remaining = MaxToolNameLength - AgentToolNames.PluginToolPrefix.Length - suffix.Length - 2;
        int pluginLength = Math.Min(sanitizedPluginName.Length, Math.Max(4, remaining / 3));
        int toolLength = Math.Max(1, remaining - pluginLength);
        return
            $"{AgentToolNames.PluginToolPrefix}{sanitizedPluginName[..Math.Min(pluginLength, sanitizedPluginName.Length)]}" +
            $"__{sanitizedToolName[..Math.Min(toolLength, sanitizedToolName.Length)]}" +
            suffix;
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
