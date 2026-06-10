using NanoAgent.Application.Utilities;
using System.Text.RegularExpressions;

namespace NanoAgent.Infrastructure.Workspaces;

internal sealed class WorkspaceIgnoreMatcher
{
    private const string GitIgnoreFileName = ".gitignore";
    private const string IgnoreFileDirectoryName = ".nanoagent";
    private const string IgnoreFileName = ".nanoignore";
    private static readonly string NanoIgnoreRelativePath = Path.Combine(IgnoreFileDirectoryName, IgnoreFileName);

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
        return Load(
            workspaceRoot,
            [GitIgnoreFileName, NanoIgnoreRelativePath]);
    }

    public static WorkspaceIgnoreMatcher Load(
        string workspaceRoot,
        IReadOnlyList<string> ignoreFilePaths)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return EmptyMatcher;
        }

        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        if (ignoreFilePaths.Count == 0)
        {
            return EmptyMatcher;
        }

        IgnoreFileCandidate[] ignoreFiles = ExpandIgnoreFiles(fullWorkspaceRoot, ignoreFilePaths);
        List<IgnoreRule> rules = [];
        foreach (IgnoreFileCandidate ignoreFile in ignoreFiles)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(ignoreFile.FullPath);
            }
            catch (Exception exception) when (IsFileSystemAccessException(exception))
            {
                continue;
            }

            rules.AddRange(lines
                .Select(line => ParseRule(
                    line,
                    ignoreFile.BaseRelativeDirectory,
                    ignoreFile.DisplayPath))
                .Where(static rule => rule is not null)
                .Select(static rule => rule!));
        }

        IgnoreRule[] normalizedRules = rules
            .ToArray();

        return normalizedRules.Length == 0
            ? EmptyMatcher
            : new WorkspaceIgnoreMatcher(fullWorkspaceRoot, normalizedRules);
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
        return GetIgnoringRule(relativePath, isDirectory) is not null;
    }

    public bool TryGetIgnoreSource(
        string fullPath,
        bool isDirectory,
        out string sourceDisplayPath)
    {
        if (_rules.Length == 0)
        {
            sourceDisplayPath = string.Empty;
            return false;
        }

        string relativePath = WorkspacePath.ToRelativePath(_workspaceRoot, fullPath);
        IgnoreRule? ignoringRule = GetIgnoringRule(relativePath, isDirectory);
        if (ignoringRule is null)
        {
            sourceDisplayPath = string.Empty;
            return false;
        }

        sourceDisplayPath = ignoringRule.SourceDisplayPath;
        return true;
    }

    public static bool MatchesGlob(
        string pattern,
        string relativePath,
        bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        IgnoreRule? rule = ParseRule(pattern, string.Empty, "<glob>");
        if (rule is null)
        {
            return false;
        }

        string[] pathSegments = NormalizePath(relativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return pathSegments.Length > 0 && Matches(rule, pathSegments, isDirectory);
    }

    private IgnoreRule? GetIgnoringRule(
        string relativePath,
        bool isDirectory)
    {
        if (_rules.Length == 0)
        {
            return null;
        }

        string normalizedPath = NormalizePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            string.Equals(normalizedPath, ".", StringComparison.Ordinal))
        {
            return null;
        }

        string[] pathSegments = normalizedPath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        IgnoreRule? ignoringRule = null;
        foreach (IgnoreRule rule in _rules)
        {
            if (!Matches(rule, pathSegments, isDirectory))
            {
                continue;
            }

            ignoringRule = rule.Negated
                ? null
                : rule;
        }

        return ignoringRule;
    }

    private static IgnoreRule? ParseRule(
        string line,
        string baseRelativeDirectory,
        string sourceDisplayPath)
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

        string[] baseSegments = GetPathSegments(baseRelativeDirectory);
        string[] segments = normalizedPattern.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        bool hasSlash = segments.Length > 1;
        string[] matchSegments = hasSlash
            ? [.. baseSegments, .. segments]
            : segments;

        return new IgnoreRule(
            negated,
            directoryOnly,
            hasSlash,
            baseSegments,
            matchSegments,
            matchSegments.Select(CreateSegmentRegex).ToArray(),
            sourceDisplayPath);
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

        if (!StartsWithSegments(pathSegments, rule.BasePathSegments))
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
        int startIndex = rule.BasePathSegments.Length;
        int segmentCount = rule.DirectoryOnly && !isDirectory
            ? pathSegments.Count - 1
            : pathSegments.Count;

        for (int index = startIndex; index < segmentCount; index++)
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

    private static bool StartsWithSegments(
        IReadOnlyList<string> pathSegments,
        IReadOnlyList<string> prefixSegments)
    {
        if (prefixSegments.Count == 0)
        {
            return true;
        }

        if (pathSegments.Count < prefixSegments.Count)
        {
            return false;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        for (int index = 0; index < prefixSegments.Count; index++)
        {
            if (!string.Equals(pathSegments[index], prefixSegments[index], comparison))
            {
                return false;
            }
        }

        return true;
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

    private static string[] GetPathSegments(string relativePath)
    {
        string normalized = NormalizePath(relativePath).Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, ".", StringComparison.Ordinal))
        {
            return [];
        }

        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static IgnoreFileCandidate[] ExpandIgnoreFiles(
        string workspaceRoot,
        IReadOnlyList<string> ignoreFilePaths)
    {
        List<IgnoreFileCandidate> candidates = [];
        foreach (string ignoreFilePath in ignoreFilePaths)
        {
            if (string.IsNullOrWhiteSpace(ignoreFilePath))
            {
                continue;
            }

            string normalizedIgnoreFilePath = NormalizePath(ignoreFilePath);
            if (string.Equals(normalizedIgnoreFilePath, GitIgnoreFileName, GetPathComparison()))
            {
                candidates.AddRange(DiscoverGitIgnoreFiles(workspaceRoot));
                continue;
            }

            string fullIgnoreFilePath = Path.GetFullPath(
                Path.IsPathRooted(ignoreFilePath)
                    ? ignoreFilePath
                    : Path.Combine(workspaceRoot, ignoreFilePath.Trim()));

            if (!WorkspacePath.IsSamePathOrDescendant(workspaceRoot, fullIgnoreFilePath) ||
                !File.Exists(fullIgnoreFilePath))
            {
                continue;
            }

            string displayPath = WorkspacePath.ToRelativePath(workspaceRoot, fullIgnoreFilePath);
            string baseRelativeDirectory = string.Equals(
                NormalizePath(displayPath),
                NormalizePath(NanoIgnoreRelativePath),
                GetPathComparison())
                ? string.Empty
                : GetRelativeDirectory(displayPath);
            candidates.Add(new IgnoreFileCandidate(
                fullIgnoreFilePath,
                baseRelativeDirectory,
                displayPath));
        }

        StringComparer pathComparer = WorkspacePath.GetPathComparer();
        return candidates
            .DistinctBy(static candidate => candidate.DisplayPath, pathComparer)
            .OrderBy(static candidate => GetPathSegments(candidate.BaseRelativeDirectory).Length)
            .ThenBy(static candidate => candidate.DisplayPath, pathComparer)
            .ToArray();
    }

    private static IEnumerable<IgnoreFileCandidate> DiscoverGitIgnoreFiles(string workspaceRoot)
    {
        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(workspaceRoot);

        while (pendingDirectories.Count > 0)
        {
            string currentDirectory = pendingDirectories.Pop();

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch (Exception exception) when (IsFileSystemAccessException(exception))
            {
                continue;
            }

            foreach (string directory in directories)
            {
                pendingDirectories.Push(directory);
            }

            IEnumerable<string> ignoreFiles;
            try
            {
                ignoreFiles = Directory.EnumerateFiles(currentDirectory, GitIgnoreFileName, SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (IsFileSystemAccessException(exception))
            {
                continue;
            }

            foreach (string ignoreFile in ignoreFiles)
            {
                string displayPath = WorkspacePath.ToRelativePath(workspaceRoot, ignoreFile);
                yield return new IgnoreFileCandidate(
                    ignoreFile,
                    GetRelativeDirectory(displayPath),
                    displayPath);
            }
        }
    }

    private static string GetRelativeDirectory(string relativePath)
    {
        string? directory = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        string normalized = NormalizePath(directory).Trim('/');
        return string.Equals(normalized, ".", StringComparison.Ordinal)
            ? string.Empty
            : normalized;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
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
        string[] BasePathSegments,
        string[] PatternSegments,
        Regex[] SegmentRegexes,
        string SourceDisplayPath);

    private sealed record IgnoreFileCandidate(
        string FullPath,
        string BaseRelativeDirectory,
        string DisplayPath);
}
