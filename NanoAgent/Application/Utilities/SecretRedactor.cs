using System.Text.RegularExpressions;

namespace NanoAgent.Application.Utilities;

public static partial class SecretRedactor
{
    /// <summary>
    /// Gets or sets whether secret redaction is enabled. Defaults to <see langword="false"/>.
    /// When <see langword="false"/>, <see cref="Redact"/> and <see cref="RedactEnvironmentFileContent"/>
    /// return the input value without modification.
    /// </summary>
    public static bool IsEnabled { get; set; } = false;
    private const string Redacted = "<redacted>";
    private const string RedactedPrivateKey = "<redacted:private-key>";

    public static string Redact(string? value)
    {
        if (!IsEnabled)
        {
            return value ?? string.Empty;
        }

        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string redacted = PrivateKeyBlockRegex().Replace(value, RedactedPrivateKey);
        redacted = BearerTokenRegex().Replace(redacted, "Bearer " + Redacted);
        redacted = OpenAiKeyRegex().Replace(redacted, Redacted);
        redacted = GitHubTokenRegex().Replace(redacted, Redacted);
        redacted = GoogleApiKeyRegex().Replace(redacted, Redacted);
        redacted = SensitiveAssignmentRegex().Replace(
            redacted,
            match => $"{match.Groups[1].Value}{Redacted}");
        return redacted;
    }

    public static string RedactEnvironmentFileContent(string? value)
    {
        if (!IsEnabled)
        {
            return value ?? string.Empty;
        }

        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string redacted = EnvironmentFileAssignmentRegex().Replace(
            value,
            match => $"{match.Groups[1].Value}{Redacted}");
        return Redact(redacted);
    }

    public static bool IsEnvironmentFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string[] segments = path.Trim()
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string fileName = segments.Length == 0
            ? path.Trim()
            : segments[^1];
        return fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(
        @"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----.*?-----END [A-Z0-9 ]*PRIVATE KEY-----",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyBlockRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_-]{8,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiKeyRegex();

    [GeneratedRegex(@"\b(?:(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,})\b", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"\bAIza[0-9A-Za-z_-]{16,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex GoogleApiKeyRegex();

    [GeneratedRegex(@"\b([A-Za-z0-9_.-]*(?:api[_-]?key|access[_-]?token|refresh[_-]?token|password|passwd|authorization|credential|client[_-]?secret|private[_-]?key|token(?:[_-]?(?:key|token|secret|id|value|string))?|secret(?:[_-]?(?:key|token|secret|id|value|string))?)\b\s*[:=]\s*[""']?)[^ \t\r\n\\,\""'}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(@"(?m)^(\s*(?:export\s+)?[A-Za-z_][A-Za-z0-9_]*\s*=\s*).+$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentFileAssignmentRegex();
}
