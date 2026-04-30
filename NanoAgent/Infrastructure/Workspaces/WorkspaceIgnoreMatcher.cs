using NanoAgent.Application.Utilities;
using System.Text.RegularExpressions;

namespace NanoAgent.Infrastructure.Workspaces;

internal sealed class WorkspaceIgnoreMatcher
{
    private const string IgnoreFileDirectoryName = ".nanoagent";
    private const string IgnoreFileName = ".nanoignore";

    private static readonly WorkspaceIgnoreMatcher EmptyMatcher = new(
        string.Empty,
        []);

    private readonly IgnoreRule[] _rules;
    private readonly string _workspaceRoot;

    private WorkspaceIgnoreMatcher(
        string workspaceRoot,
        IgnoreRule[] rules)
    {
        _workspaceRoot = workspaceRoot;
        _rules = rules;
    }

    public bool HasRules => _rules.Length > 0;

    public static WorkspaceIgnoreMatcher Load(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return EmptyMatcher;
        }

        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        string ignoreFilePath = Path.Combine(
            fullWorkspaceRoot,
            IgnoreFileDirectoryName,
            IgnoreFileName);
        if (!File.Exists(ignoreFilePath))
        {
            return EmptyMatcher;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(ignoreFilePath);
        }
        catch (Exception exception) when (IsFileSystemAccessException(exception))
        {
            return EmptyMatcher;
        }

        IgnoreRule[] rules = lines
            .Select(ParseRule)
            .Where(static rule => rule is not null)
            .Select(static rule => rule!)
            .ToArray();

        return rules.Length == 0
            ? EmptyMatcher
            : new WorkspaceIgnoreMatcher(fullWorkspaceRoot, rules);
    }

    public bool IsIgnored(
        string fullPath,
        bool isDirectory)
    {
        if (_rules.Length == 0)
        {
            return false;
        }

        string relativePath = WorkspacePath.ToRelativePath(_workspaceRoot, fullPath);
        return IsIgnoredRelative(relativePath, isDirectory);
    }

    public bool IsIgnoredRelative(
        string relativePath,
        bool isDirectory)
    {
        if (_rules.Length == 0)
        {
            return false;
        }

        string normalizedPath = NormalizePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            string.Equals(normalizedPath, ".", StringComparison.Ordinal))
        {
            return false;
        }

        string[] pathSegments = normalizedPath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        bool ignored = false;
        foreach (IgnoreRule rule in _rules)
        {
            if (Matches(rule, pathSegments, isDirectory))
            {
                ignored = !rule.Negated;
            }
        }

        return ignored;
    }

    private static IgnoreRule? ParseRule(string line)
    {
        string trimmedLine = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmedLine))
        {
            return null;
        }

        if (trimmedLine.StartsWith(@"\#", StringComparison.Ordinal))
        {
            trimmedLine = trimmedLine[1..];
        }
        else if (trimmedLine.StartsWith('#'))
        {
            return null;
        }

        bool negated = false;
        if (trimmedLine.StartsWith(@"\!", StringComparison.Ordinal))
        {
            trimmedLine = trimmedLine[1..];
        }
        else if (trimmedLine.StartsWith('!'))
        {
            negated = true;
            trimmedLine = trimmedLine[1..].TrimStart();
        }

        string normalizedPattern = NormalizePath(trimmedLine);
        while (normalizedPattern.StartsWith('/'))
        {
            normalizedPattern = normalizedPattern[1..];
        }

        bool directoryOnly = normalizedPattern.EndsWith('/');
        normalizedPattern = normalizedPattern.Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            return null;
        }

        string[] segments = normalizedPattern.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        bool hasSlash = segments.Length > 1;

        return new IgnoreRule(
            negated,
            directoryOnly,
            hasSlash,
            segments,
            segments.Select(CreateSegmentRegex).ToArray());
    }

    private static bool Matches(
        IgnoreRule rule,
        IReadOnlyList<string> pathSegments,
        bool isDirectory)
    {
        if (pathSegments.Count == 0)
        {
            return false;
        }

        if (!rule.HasSlash)
        {
            return MatchesSingleSegmentRule(rule, pathSegments, isDirectory);
        }

        return MatchesPathRule(rule, pathSegments, isDirectory);
    }

    private static bool MatchesSingleSegmentRule(
        IgnoreRule rule,
        IReadOnlyList<string> pathSegments,
        bool isDirectory)
    {
        Regex segmentRegex = rule.SegmentRegexes[0];
        int segmentCount = rule.DirectoryOnly && !isDirectory
            ? pathSegments.Count - 1
            : pathSegments.Count;

        for (int index = 0; index < segmentCount; index++)
        {
            if (segmentRegex.IsMatch(pathSegments[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPathRule(
        IgnoreRule rule,
        IReadOnlyList<string> pathSegments,
        bool isDirectory)
    {
        if (!rule.DirectoryOnly &&
            MatchesSegments(rule, pathSegments))
        {
            return true;
        }

        int directoryPrefixCount = isDirectory
            ? pathSegments.Count
            : pathSegments.Count - 1;

        for (int count = 1; count <= directoryPrefixCount; count++)
        {
            if (MatchesSegments(rule, pathSegments.Take(count).ToArray()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesSegments(
        IgnoreRule rule,
        IReadOnlyList<string> pathSegments)
    {
        return MatchesSegments(
            rule,
            patternIndex: 0,
            pathSegments,
            pathIndex: 0);
    }

    private static bool MatchesSegments(
        IgnoreRule rule,
        int patternIndex,
        IReadOnlyList<string> pathSegments,
        int pathIndex)
    {
        while (true)
        {
            if (patternIndex >= rule.PatternSegments.Length)
            {
                return pathIndex >= pathSegments.Count;
            }

            string patternSegment = rule.PatternSegments[patternIndex];
            if (string.Equals(patternSegment, "**", StringComparison.Ordinal))
            {
                if (MatchesSegments(
                        rule,
                        patternIndex + 1,
                        pathSegments,
                        pathIndex))
                {
                    return true;
                }

                if (pathIndex >= pathSegments.Count)
                {
                    return false;
                }

                pathIndex++;
                continue;
            }

            if (pathIndex >= pathSegments.Count ||
                !rule.SegmentRegexes[patternIndex].IsMatch(pathSegments[pathIndex]))
            {
                return false;
            }

            patternIndex++;
            pathIndex++;
        }
    }

    private static Regex CreateSegmentRegex(string patternSegment)
    {
        if (string.Equals(patternSegment, "**", StringComparison.Ordinal))
        {
            return new Regex("^.*$", GetRegexOptions());
        }

        return new Regex(
            "^" + ConvertSegmentGlobToRegex(patternSegment) + "$",
            GetRegexOptions());
    }

    private static string ConvertSegmentGlobToRegex(string value)
    {
        StringWriter writer = new();
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '*':
                    writer.Write(".*");
                    break;

                case '?':
                    writer.Write('.');
                    break;

                case '[':
                    if (TryReadCharacterClass(value, index, out string? characterClass, out int endIndex))
                    {
                        writer.Write(characterClass);
                        index = endIndex;
                    }
                    else
                    {
                        writer.Write(@"\[");
                    }

                    break;

                default:
                    writer.Write(Regex.Escape(character.ToString()));
                    break;
            }
        }

        return writer.ToString();
    }

    private static bool TryReadCharacterClass(
        string value,
        int startIndex,
        out string? characterClass,
        out int endIndex)
    {
        characterClass = null;
        endIndex = startIndex;

        int closingIndex = value.IndexOf(']', startIndex + 1);
        if (closingIndex <= startIndex + 1)
        {
            return false;
        }

        string content = value[(startIndex + 1)..closingIndex];
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        if (content[0] == '!')
        {
            content = "^" + content[1..];
        }
        else if (content[0] == '^')
        {
            content = @"\^" + content[1..];
        }

        characterClass = "[" + content.Replace(@"\", @"\\", StringComparison.Ordinal) + "]";
        endIndex = closingIndex;
        return true;
    }

    private static RegexOptions GetRegexOptions()
    {
        RegexOptions options = RegexOptions.CultureInvariant;
        if (OperatingSystem.IsWindows())
        {
            options |= RegexOptions.IgnoreCase;
        }

        return options;
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('\\', '/');
    }

    private static bool IsFileSystemAccessException(Exception exception)
    {
        return exception is UnauthorizedAccessException or
            IOException or
            PathTooLongException or
            System.Security.SecurityException;
    }

    private sealed record IgnoreRule(
        bool Negated,
        bool DirectoryOnly,
        bool HasSlash,
        string[] PatternSegments,
        Regex[] SegmentRegexes);
}
