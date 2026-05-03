namespace NanoAgent.Application.Models;

public sealed record RepoMemoryDocumentDefinition(
    string Name,
    string FileName,
    string Title,
    string Description);

public static class RepoMemoryDocuments
{
    public const string DirectoryPath = ".nanoagent/memory";

    private static readonly RepoMemoryDocumentDefinition[] Documents =
    [
        new(
            "architecture",
            "architecture.md",
            "Architecture",
            "Major components, boundaries, data flow, and integration points."),
        new(
            "conventions",
            "conventions.md",
            "Conventions",
            "Coding, naming, formatting, review, and workflow conventions."),
        new(
            "decisions",
            "decisions.md",
            "Decisions",
            "Durable technical decisions, context, and consequences."),
        new(
            "known-issues",
            "known-issues.md",
            "Known Issues",
            "Known bugs, limitations, risky areas, and workarounds."),
        new(
            "test-strategy",
            "test-strategy.md",
            "Test Strategy",
            "Expected test layers, important commands, and validation guidance.")
    ];

    public static IReadOnlyList<RepoMemoryDocumentDefinition> All => Documents;

    public static string GetRelativePath(RepoMemoryDocumentDefinition document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return $"{DirectoryPath}/{document.FileName}";
    }

    public static string CreateTemplate(RepoMemoryDocumentDefinition document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return
            $$"""
            # {{document.Title}}

            {{document.Description}}

            Keep this file focused on durable, team-reviewed memory. NanoAgent treats it as repo-scoped context that your team can inspect, diff, and version-control.
            """;
    }

    public static bool IsTemplateContent(
        RepoMemoryDocumentDefinition document,
        string content)
    {
        ArgumentNullException.ThrowIfNull(document);

        return string.Equals(
            NormalizeContentForComparison(CreateTemplate(document)),
            NormalizeContentForComparison(content),
            StringComparison.Ordinal);
    }

    public static bool IsMemoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalizedPath = NormalizePath(path);
        return normalizedPath.Equals(DirectoryPath, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(DirectoryPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryResolve(
        string? value,
        out RepoMemoryDocumentDefinition? document)
    {
        document = null;
        string normalizedValue = NormalizeDocumentKey(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return false;
        }

        document = Documents.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, normalizedValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.FileName, normalizedValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                Path.GetFileNameWithoutExtension(candidate.FileName),
                normalizedValue,
                StringComparison.OrdinalIgnoreCase));

        return document is not null;
    }

    private static string NormalizeDocumentKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : NormalizePath(value.Trim());
    }

    private static string NormalizePath(string path)
    {
        return path
            .Trim()
            .Replace('\\', '/')
            .Trim('/');
    }

    private static string NormalizeContentForComparison(string content)
    {
        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }
}
