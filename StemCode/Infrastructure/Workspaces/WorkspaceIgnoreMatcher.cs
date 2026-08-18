using StemCode.Application.Utilities;

namespace StemCode.Infrastructure.Workspaces;

internal sealed class WorkspaceIgnoreMatcher
{
    private const string IgnoreFileDirectoryName = ".stemcode";
    private const string IgnoreFileName = ".stemcodeignore";
    internal static readonly string StemCodeIgnoreRelativePath = Path.Combine(IgnoreFileDirectoryName, IgnoreFileName);

    private const string GitIgnoreFileName = ".gitignore";
    private static readonly string GitIgnoreRelativePath = GitIgnoreFileName;

    // gitignore-style matching is case-insensitive on Windows and
    // case-sensitive everywhere else. This mirrors the original RegexOptions
    // (CultureInvariant | IgnoreCase) and is reused across every match.
    private static readonly StringComparison SegmentComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

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
            [StemCodeIgnoreRelativePath]);
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

    /// <summary>
    /// Loads the ignore rules used by the search tools. The project's existing
    /// <c>.gitignore</c> is respected first (it is the primary, project-level
    /// ignore source), followed by the StemCode-specific
    /// <c>.stemcode/.stemcodeignore</c> rules. A later <c>.stemcodeignore</c>
    /// negation can still re-include a path that <c>.gitignore</c> ignores.
    /// </summary>
    public static WorkspaceIgnoreMatcher LoadWithProjectIgnoreRules(string workspaceRoot)
    {
        return Load(
            workspaceRoot,
            [GitIgnoreRelativePath, StemCodeIgnoreRelativePath]);
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
        return CompiledGlob.Parse(pattern)
            .Matches(relativePath.AsSpan(), isDirectory);
    }

    /// <summary>
    /// A glob pattern parsed once so it can be matched against many candidate
    /// paths without re-parsing. Call <see cref="Matches"/> on the hot path;
    /// it allocates nothing beyond a stack-only <see cref="ReadOnlySpan{T}"/>
    /// view of the candidate path.
    /// </summary>
    internal readonly struct CompiledGlob
    {
        private readonly IgnoreRule? _rule;

        private CompiledGlob(IgnoreRule? rule) => _rule = rule;

        /// <summary>
        /// False when the pattern was empty/whitespace or did not parse into a
        /// usable rule. Such a glob never matches, mirroring
        /// <see cref="MatchesGlob"/>.
        /// </summary>
        public bool IsValid => _rule is not null;

        public static CompiledGlob Parse(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return default;
            }

            return new CompiledGlob(ParseRule(pattern, string.Empty, "<glob>"));
        }

        public static bool TryParse(string pattern, out CompiledGlob glob)
        {
            glob = Parse(pattern);
            return glob.IsValid;
        }

        /// <summary>
        /// Returns true when <paramref name="relativePath"/> matches this glob.
        /// The path is treated as already relative and may use either '/' or '\'
        /// separators. Allocation free.
        /// </summary>
        public bool Matches(ReadOnlySpan<char> relativePath, bool isDirectory)
        {
            if (_rule is null)
            {
                return false;
            }

            ReadOnlySpan<char> normalizedPath = TrimPathSpan(relativePath);
            if (normalizedPath.IsEmpty ||
                normalizedPath.Equals(".", StringComparison.Ordinal))
            {
                return false;
            }

            return WorkspaceIgnoreMatcher.Matches(_rule, normalizedPath, isDirectory);
        }
    }

    private IgnoreRule? GetIgnoringRule(
        string relativePath,
        bool isDirectory)
    {
        if (_rules.Length == 0)
        {
            return null;
        }

        ReadOnlySpan<char> normalizedPath = TrimPathSpan(relativePath);
        if (normalizedPath.IsEmpty ||
            normalizedPath.Equals(".", StringComparison.Ordinal))
        {
            return null;
        }

        IgnoreRule? ignoringRule = null;
        foreach (IgnoreRule rule in _rules)
        {
            if (!Matches(rule, normalizedPath, isDirectory))
            {
                continue;
            }

            ignoringRule = rule.Negated
                ? null
                : rule;
        }

        return ignoringRule;
    }

    // ---------------------------------------------------------------------
    // Span-based matching core (allocation free on the hot path)
    // ---------------------------------------------------------------------

    private static bool Matches(
        IgnoreRule rule,
        ReadOnlySpan<char> path,
        bool isDirectory)
    {
        int totalSegments = CountSegments(path);
        if (totalSegments == 0)
        {
            return false;
        }

        if (!StartsWithSegments(path, rule.BasePathSegments))
        {
            return false;
        }

        if (!rule.HasSlash)
        {
            return MatchesSingleSegmentRule(rule, path, totalSegments, isDirectory);
        }

        return MatchesPathRule(rule, path, totalSegments, isDirectory);
    }

    private static bool MatchesSingleSegmentRule(
        IgnoreRule rule,
        ReadOnlySpan<char> path,
        int totalSegments,
        bool isDirectory)
    {
        ReadOnlySpan<char> pattern = rule.PatternSegments[0].AsSpan();
        int startIndex = rule.BasePathSegments.Length;
        int segmentCount = rule.DirectoryOnly && !isDirectory
            ? totalSegments - 1
            : totalSegments;

        int offset = 0;
        int segmentIndex = 0;
        while (TryGetSegment(path, ref offset, out ReadOnlySpan<char> segment))
        {
            if (segmentIndex >= startIndex &&
                segmentIndex < segmentCount &&
                MatchSegmentGlob(pattern, segment, SegmentComparison))
            {
                return true;
            }

            segmentIndex++;
        }

        return false;
    }

    private static bool MatchesPathRule(
        IgnoreRule rule,
        ReadOnlySpan<char> path,
        int totalSegments,
        bool isDirectory)
    {
        if (!rule.DirectoryOnly &&
            MatchesSegments(rule, patternIndex: 0, path, offset: 0, totalSegments))
        {
            return true;
        }

        // A directory-only (or path) rule also ignores every path beneath the
        // matched directory. Instead of allocating Take(count).ToArray() for
        // each prefix, we cap how many leading segments the matcher may consume.
        int directoryPrefixCount = isDirectory
            ? totalSegments
            : totalSegments - 1;

        for (int count = 1; count <= directoryPrefixCount; count++)
        {
            if (MatchesSegments(rule, patternIndex: 0, path, offset: 0, count))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesSegments(
        IgnoreRule rule,
        int patternIndex,
        ReadOnlySpan<char> path,
        int offset,
        int remainingSegments)
    {
        while (true)
        {
            if (patternIndex >= rule.PatternSegments.Length)
            {
                return remainingSegments == 0;
            }

            ReadOnlySpan<char> patternSegment = rule.PatternSegments[patternIndex].AsSpan();
            if (patternSegment.Equals("**", StringComparison.Ordinal))
            {
                if (MatchesSegments(rule, patternIndex + 1, path, offset, remainingSegments))
                {
                    return true;
                }

                if (remainingSegments == 0)
                {
                    return false;
                }

                if (!TryGetSegment(path, ref offset, out _))
                {
                    return false;
                }

                remainingSegments--;
                continue;
            }

            if (remainingSegments == 0)
            {
                return false;
            }

            if (!TryGetSegment(path, ref offset, out ReadOnlySpan<char> pathSegment))
            {
                return false;
            }

            remainingSegments--;
            if (!MatchSegmentGlob(patternSegment, pathSegment, SegmentComparison))
            {
                return false;
            }

            patternIndex++;
        }
    }

    private static bool StartsWithSegments(
        ReadOnlySpan<char> path,
        string[] prefixSegments)
    {
        if (prefixSegments.Length == 0)
        {
            return true;
        }

        int offset = 0;
        for (int index = 0; index < prefixSegments.Length; index++)
        {
            if (!TryGetSegment(path, ref offset, out ReadOnlySpan<char> segment))
            {
                return false;
            }

            if (!segment.Equals(prefixSegments[index].AsSpan(), SegmentComparison))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the next non-empty path segment, advancing <paramref name="offset"/>
    /// past it (and any trailing separators). Both '/' and '\' are accepted so
    /// Windows paths do not need to be normalized into a new string first.
    /// Allocation free.
    /// </summary>
    private static bool TryGetSegment(
        ReadOnlySpan<char> path,
        ref int offset,
        out ReadOnlySpan<char> segment)
    {
        while (offset < path.Length && IsPathSeparator(path[offset]))
        {
            offset++;
        }

        if (offset >= path.Length)
        {
            segment = default;
            return false;
        }

        int start = offset;
        while (offset < path.Length && !IsPathSeparator(path[offset]))
        {
            offset++;
        }

        segment = path.Slice(start, offset - start);
        return true;
    }

    private static bool IsPathSeparator(char value)
    {
        return value is '/' or '\\';
    }

    private static int CountSegments(ReadOnlySpan<char> path)
    {
        int count = 0;
        int offset = 0;
        while (TryGetSegment(path, ref offset, out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Matches a single gitignore segment glob (no '/') against a path segment.
    /// Supports '*' (any run), '?' (any single char), and '[...]' character
    /// classes. This replaces the previous per-segment <see cref="Regex"/> so
    /// matching allocates nothing on the heap.
    /// </summary>
    private static bool MatchSegmentGlob(
        ReadOnlySpan<char> pattern,
        ReadOnlySpan<char> text,
        StringComparison comparison)
    {
        bool ignoreCase = comparison == StringComparison.OrdinalIgnoreCase;
        int p = 0;
        int t = 0;
        int star = -1;
        int starText = -1;

        while (t < text.Length)
        {
            if (p >= pattern.Length)
            {
                if (star < 0)
                {
                    return false;
                }

                p = star + 1;
                starText++;
                t = starText;
                continue;
            }

            char patternChar = pattern[p];
            if (patternChar == '*')
            {
                star = p;
                starText = t;
                p++;
                continue;
            }

            if (MatchToken(pattern, ref p, text, t, ignoreCase))
            {
                t++;
                continue;
            }

            if (star < 0)
            {
                return false;
            }

            p = star + 1;
            starText++;
            t = starText;
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool MatchToken(
        ReadOnlySpan<char> pattern,
        ref int p,
        ReadOnlySpan<char> text,
        int t,
        bool ignoreCase)
    {
        char patternChar = pattern[p];
        if (patternChar == '?')
        {
            p++;
            return true;
        }

        if (patternChar == '[')
        {
            int relativeEnd = pattern[(p + 1)..].IndexOf(']');
            int end = relativeEnd < 0
                ? -1
                : p + 1 + relativeEnd;
            if (end > p + 1)
            {
                ReadOnlySpan<char> content = pattern.Slice(p + 1, end - p - 1);
                p = end + 1;
                return IsCharInClass(content, text[t], ignoreCase);
            }

            // Unterminated class: treat '[' as a literal character.
            p++;
            return CharEquals('[', text[t], ignoreCase);
        }

        p++;
        return CharEquals(patternChar, text[t], ignoreCase);
    }

    private static bool IsCharInClass(
        ReadOnlySpan<char> content,
        char c,
        bool ignoreCase)
    {
        bool negated = false;
        int start = 0;
        if (content.Length > 0 && content[0] == '!')
        {
            negated = true;
            start = 1;
        }

        bool found = false;
        int i = start;
        while (i < content.Length)
        {
            if (i + 2 < content.Length &&
                content[i + 1] == '-' &&
                content[i + 2] != ']')
            {
                if (CharInRange(c, content[i], content[i + 2], ignoreCase))
                {
                    found = true;
                    break;
                }

                i += 3;
            }
            else
            {
                if (CharEquals(content[i], c, ignoreCase))
                {
                    found = true;
                    break;
                }

                i++;
            }
        }

        return negated ? !found : found;
    }

    private static bool CharInRange(char c, char low, char high, bool ignoreCase)
    {
        if (ignoreCase)
        {
            c = char.ToLowerInvariant(c);
            low = char.ToLowerInvariant(low);
            high = char.ToLowerInvariant(high);
        }

        return c >= low && c <= high;
    }

    private static bool CharEquals(char a, char b, bool ignoreCase)
    {
        if (a == b)
        {
            return true;
        }

        return ignoreCase && char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
    }

    // ---------------------------------------------------------------------
    // Rule parsing (load time only)
    // ---------------------------------------------------------------------

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
            sourceDisplayPath);
    }

    // ---------------------------------------------------------------------
    // Path normalization helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns a trimmed view of <paramref name="path"/> without allocating.
    /// Segment scanning accepts both '/' and '\', so no separator-replacement
    /// string is required on the matching hot path.
    /// </summary>
    private static ReadOnlySpan<char> TrimPathSpan(ReadOnlySpan<char> path)
    {
        ReadOnlySpan<char> span = path;
        int start = 0;
        while (start < span.Length && char.IsWhiteSpace(span[start]))
        {
            start++;
        }

        int end = span.Length;
        while (end > start && char.IsWhiteSpace(span[end - 1]))
        {
            end--;
        }

        return span.Slice(start, end - start);
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
                NormalizePath(StemCodeIgnoreRelativePath),
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
        string SourceDisplayPath);

    private sealed record IgnoreFileCandidate(
        string FullPath,
        string BaseRelativeDirectory,
        string DisplayPath);
}
