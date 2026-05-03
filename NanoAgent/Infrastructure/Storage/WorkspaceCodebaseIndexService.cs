using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Workspaces;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Storage;

internal sealed class WorkspaceCodebaseIndexService : ICodebaseIndexService
{
    private const int CurrentIndexVersion = 1;
    private const int MaxIndexedFiles = 5_000;
    private const int MaxIndexFileBytes = 262_144;
    private const int MaxTermsPerFile = 1_200;
    private const int MaxSymbolsPerFile = 80;
    private const int MaxSnippetsPerMatch = 3;
    private const int MaxSnippetCharacters = 220;
    private const int MaxStatusSamples = 8;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly string[] IgnoreFilePaths =
    [
        ".gitignore",
        Path.Combine(".nanoagent", ".nanoignore")
    ];

    private static readonly HashSet<string> DefaultIgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".hg",
        ".idea",
        ".svn",
        ".vs",
        "bin",
        "build",
        "coverage",
        "dist",
        "node_modules",
        "obj",
        "packages",
        "publish",
        "TestResults"
    };

    private static readonly string[] DefaultIgnoredPathPrefixes =
    [
        ".nanoagent/cache/",
        ".nanoagent/logs/",
        ".nanoagent/sessions/",
        ".nanoagent/temp/",
        ".nanoagent/tmp/"
    ];

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z",
        ".a",
        ".avi",
        ".bmp",
        ".class",
        ".dll",
        ".dmg",
        ".doc",
        ".docx",
        ".exe",
        ".gif",
        ".gz",
        ".ico",
        ".jar",
        ".jpeg",
        ".jpg",
        ".lockb",
        ".mov",
        ".mp3",
        ".mp4",
        ".o",
        ".pdb",
        ".pdf",
        ".png",
        ".so",
        ".sqlite",
        ".tar",
        ".webp",
        ".woff",
        ".woff2",
        ".zip"
    };

    private readonly TimeProvider _timeProvider;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public WorkspaceCodebaseIndexService(
        IWorkspaceRootProvider workspaceRootProvider,
        TimeProvider? timeProvider = null)
    {
        _workspaceRootProvider = workspaceRootProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CodebaseIndexBuildResult> BuildAsync(
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch stopwatch = Stopwatch.StartNew();
        string workspaceRoot = GetWorkspaceRoot();
        string indexPath = GetIndexPath(workspaceRoot);
        WorkspaceScan scan = ScanWorkspace(workspaceRoot, cancellationToken);
        CodebaseIndexDocument? existingIndex = await LoadIndexAsync(
            indexPath,
            cancellationToken);
        Dictionary<string, CodebaseIndexedFileDocument> existingFiles = (existingIndex?.Files ?? [])
            .Where(static file => file is not null && !string.IsNullOrWhiteSpace(file.Path))
            .ToDictionary(static file => file.Path, StringComparer.OrdinalIgnoreCase);

        List<CodebaseIndexedFileDocument> indexedFiles = [];
        int added = 0;
        int updated = 0;
        int reused = 0;
        int skipped = scan.SkippedFileCount;

        foreach (CodebaseIndexCandidate candidate in scan.Files.Take(MaxIndexedFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!force &&
                existingFiles.TryGetValue(candidate.RelativePath, out CodebaseIndexedFileDocument? existingFile) &&
                HasSameMetadata(candidate, existingFile))
            {
                indexedFiles.Add(existingFile);
                reused++;
                continue;
            }

            CodebaseIndexedFileDocument? indexedFile = await IndexFileAsync(
                candidate,
                cancellationToken);
            if (indexedFile is null)
            {
                skipped++;
                continue;
            }

            indexedFiles.Add(indexedFile);
            if (existingFiles.ContainsKey(candidate.RelativePath))
            {
                updated++;
            }
            else
            {
                added++;
            }
        }

        if (scan.Files.Count > MaxIndexedFiles)
        {
            skipped += scan.Files.Count - MaxIndexedFiles;
        }

        HashSet<string> currentPaths = scan.Files
            .Take(MaxIndexedFiles)
            .Select(static file => file.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int removed = existingFiles.Keys.Count(path => !currentPaths.Contains(path));

        DateTimeOffset builtAtUtc = _timeProvider.GetUtcNow();
        CodebaseIndexDocument index = new()
        {
            Version = CurrentIndexVersion,
            BuiltAtUtc = builtAtUtc,
            WorkspaceRoot = workspaceRoot,
            Files = indexedFiles
                .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        await SaveIndexAsync(
            indexPath,
            index,
            cancellationToken);

        stopwatch.Stop();
        return new CodebaseIndexBuildResult(
            ToWorkspaceRelativePath(workspaceRoot, indexPath),
            builtAtUtc,
            index.Files.Count,
            added,
            updated,
            removed,
            reused,
            skipped,
            stopwatch.ElapsedMilliseconds);
    }

    public async Task<CodebaseIndexStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = GetWorkspaceRoot();
        string indexPath = GetIndexPath(workspaceRoot);
        WorkspaceScan scan = ScanWorkspace(workspaceRoot, cancellationToken);
        CodebaseIndexDocument? index = await LoadIndexAsync(
            indexPath,
            cancellationToken);

        if (index is null)
        {
            string[] sampleNewFiles = scan.Files
                .Select(static file => file.RelativePath)
                .Take(MaxStatusSamples)
                .ToArray();
            return new CodebaseIndexStatusResult(
                ToWorkspaceRelativePath(workspaceRoot, indexPath),
                Exists: false,
                IsStale: scan.Files.Count > 0,
                BuiltAtUtc: null,
                IndexedFileCount: 0,
                WorkspaceFileCount: scan.Files.Count,
                NewFileCount: scan.Files.Count,
                ChangedFileCount: 0,
                DeletedFileCount: 0,
                SkippedFileCount: scan.SkippedFileCount,
                SampleNewFiles: sampleNewFiles,
                SampleChangedFiles: [],
                SampleDeletedFiles: []);
        }

        Dictionary<string, CodebaseIndexedFileDocument> indexedFiles = index.Files
            .Where(static file => file is not null && !string.IsNullOrWhiteSpace(file.Path))
            .ToDictionary(static file => file.Path, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CodebaseIndexCandidate> workspaceFiles = scan.Files
            .ToDictionary(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        string[] newFiles = workspaceFiles.Keys
            .Where(path => !indexedFiles.ContainsKey(path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] changedFiles = workspaceFiles.Values
            .Where(file => indexedFiles.TryGetValue(file.RelativePath, out CodebaseIndexedFileDocument? indexedFile) &&
                           !HasSameMetadata(file, indexedFile))
            .Select(static file => file.RelativePath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] deletedFiles = indexedFiles.Keys
            .Where(path => !workspaceFiles.ContainsKey(path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CodebaseIndexStatusResult(
            ToWorkspaceRelativePath(workspaceRoot, indexPath),
            Exists: true,
            IsStale: newFiles.Length > 0 || changedFiles.Length > 0 || deletedFiles.Length > 0,
            BuiltAtUtc: index.BuiltAtUtc,
            IndexedFileCount: index.Files.Count,
            WorkspaceFileCount: scan.Files.Count,
            NewFileCount: newFiles.Length,
            ChangedFileCount: changedFiles.Length,
            DeletedFileCount: deletedFiles.Length,
            SkippedFileCount: scan.SkippedFileCount,
            SampleNewFiles: newFiles.Take(MaxStatusSamples).ToArray(),
            SampleChangedFiles: changedFiles.Take(MaxStatusSamples).ToArray(),
            SampleDeletedFiles: deletedFiles.Take(MaxStatusSamples).ToArray());
    }

    public async Task<CodebaseIndexSearchResult> SearchAsync(
        string query,
        int limit,
        bool includeSnippets,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        cancellationToken.ThrowIfCancellationRequested();

        CodebaseIndexStatusResult status = await GetStatusAsync(cancellationToken);
        bool indexWasUpdated = !status.Exists || status.IsStale;
        if (indexWasUpdated)
        {
            await BuildAsync(force: false, cancellationToken);
        }

        string workspaceRoot = GetWorkspaceRoot();
        string indexPath = GetIndexPath(workspaceRoot);
        CodebaseIndexDocument? index = await LoadIndexAsync(indexPath, cancellationToken);
        if (index is null)
        {
            return new CodebaseIndexSearchResult(
                query.Trim(),
                ToWorkspaceRelativePath(workspaceRoot, indexPath),
                indexWasUpdated,
                IndexedFileCount: 0,
                Matches: []);
        }

        string normalizedQuery = query.Trim();
        string[] queryTerms = Tokenize(normalizedQuery)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        int maxResults = Math.Clamp(limit, 1, 50);

        CodebaseIndexSearchMatch[] matches = index.Files
            .Select(file => ScoreFile(workspaceRoot, file, normalizedQuery, queryTerms, includeSnippets, cancellationToken))
            .Where(static match => match.Score > 0)
            .OrderByDescending(static match => match.Score)
            .ThenBy(static match => match.Path, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();

        return new CodebaseIndexSearchResult(
            normalizedQuery,
            ToWorkspaceRelativePath(workspaceRoot, indexPath),
            indexWasUpdated,
            index.Files.Count,
            matches);
    }

    public async Task<CodebaseIndexListResult> ListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CodebaseIndexStatusResult status = await GetStatusAsync(cancellationToken);
        if (!status.Exists || status.IsStale)
        {
            await BuildAsync(force: false, cancellationToken);
        }

        string workspaceRoot = GetWorkspaceRoot();
        string indexPath = GetIndexPath(workspaceRoot);
        CodebaseIndexDocument? index = await LoadIndexAsync(indexPath, cancellationToken);
        if (index is null)
        {
            return new CodebaseIndexListResult(
                ToWorkspaceRelativePath(workspaceRoot, indexPath),
                TotalIndexedFileCount: 0,
                ReturnedFileCount: 0,
                Files: []);
        }

        string[] files = index.Files
            .Select(static file => file.Path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 10_000))
            .ToArray();

        return new CodebaseIndexListResult(
            ToWorkspaceRelativePath(workspaceRoot, indexPath),
            index.Files.Count,
            files.Length,
            files);
    }

    private CodebaseIndexSearchMatch ScoreFile(
        string workspaceRoot,
        CodebaseIndexedFileDocument file,
        string normalizedQuery,
        IReadOnlyList<string> queryTerms,
        bool includeSnippets,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        double score = 0;
        string normalizedPath = file.Path.Replace('\\', '/');
        if (normalizedPath.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        foreach (string queryTerm in queryTerms)
        {
            if (file.Terms.TryGetValue(queryTerm, out int count))
            {
                score += Math.Log(count + 1, 2);
            }

            if (normalizedPath.Contains(queryTerm, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }

            score += file.Symbols.Count(symbol =>
                symbol.Contains(queryTerm, StringComparison.OrdinalIgnoreCase)) * 4;
        }

        CodebaseIndexSnippet[] snippets = score <= 0 || !includeSnippets
            ? []
            : CreateSnippets(workspaceRoot, file.Path, normalizedQuery, queryTerms, cancellationToken);
        if (snippets.Length > 0)
        {
            score += 2;
        }

        return new CodebaseIndexSearchMatch(
            file.Path,
            file.Language,
            Math.Round(score, 3),
            file.Symbols.Take(10).ToArray(),
            snippets);
    }

    private static CodebaseIndexSnippet[] CreateSnippets(
        string workspaceRoot,
        string relativePath,
        string normalizedQuery,
        IReadOnlyList<string> queryTerms,
        CancellationToken cancellationToken)
    {
        string fullPath = WorkspacePath.Resolve(workspaceRoot, relativePath);
        if (!File.Exists(fullPath))
        {
            return [];
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(fullPath, Encoding.UTF8);
        }
        catch (Exception exception) when (IsFileSystemAccessException(exception) ||
                                          exception is DecoderFallbackException or InvalidDataException)
        {
            return [];
        }

        List<CodebaseIndexSnippet> snippets = [];
        for (int index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string line = lines[index].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            bool matches = line.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                queryTerms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (!matches)
            {
                continue;
            }

            snippets.Add(new CodebaseIndexSnippet(
                index + 1,
                Truncate(line, MaxSnippetCharacters)));
            if (snippets.Count >= MaxSnippetsPerMatch)
            {
                break;
            }
        }

        return snippets.ToArray();
    }

    private async Task<CodebaseIndexedFileDocument?> IndexFileAsync(
        CodebaseIndexCandidate candidate,
        CancellationToken cancellationToken)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(
                candidate.FullPath,
                Encoding.UTF8,
                cancellationToken);
        }
        catch (Exception exception) when (IsFileSystemAccessException(exception) ||
                                          exception is DecoderFallbackException or InvalidDataException)
        {
            return null;
        }

        Dictionary<string, int> terms = new(StringComparer.Ordinal);
        AddPathTerms(candidate.RelativePath, terms);
        foreach (string token in Tokenize(content))
        {
            AddTerm(token, terms);
        }

        string[] symbols = ExtractSymbols(content)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSymbolsPerFile)
            .ToArray();
        foreach (string symbol in symbols)
        {
            foreach (string token in Tokenize(symbol))
            {
                AddTerm(token, terms, weight: 2);
            }
        }

        return new CodebaseIndexedFileDocument
        {
            Path = candidate.RelativePath,
            Length = candidate.Length,
            LastWriteTimeUtc = candidate.LastWriteTimeUtc,
            Sha256 = ComputeSha256(content),
            Language = GetLanguage(candidate.RelativePath),
            LineCount = CountLines(content),
            Symbols = symbols,
            Terms = terms
        };
    }

    private WorkspaceScan ScanWorkspace(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        WorkspaceIgnoreMatcher ignoreMatcher = WorkspaceIgnoreMatcher.Load(
            workspaceRoot,
            IgnoreFilePaths);
        List<CodebaseIndexCandidate> files = [];
        int skippedFileCount = 0;
        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(workspaceRoot);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directoryPath = pendingDirectories.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directoryPath);
            }
            catch (Exception exception) when (IsFileSystemAccessException(exception))
            {
                continue;
            }

            foreach (string entry in entries.Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (IsFileSystemAccessException(exception))
                {
                    continue;
                }

                bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
                string relativePath = WorkspacePath.ToRelativePath(workspaceRoot, entry);
                if (IsDefaultIgnoredPath(relativePath, isDirectory) ||
                    ignoreMatcher.IsIgnored(entry, isDirectory))
                {
                    if (!isDirectory)
                    {
                        skippedFileCount++;
                    }

                    continue;
                }

                if (isDirectory)
                {
                    if (!attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        pendingDirectories.Push(entry);
                    }

                    continue;
                }

                if (!TryCreateCandidate(entry, relativePath, out CodebaseIndexCandidate? candidate))
                {
                    skippedFileCount++;
                    continue;
                }

                files.Add(candidate!);
            }
        }

        return new WorkspaceScan(
            files.OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            skippedFileCount);
    }

    private static bool TryCreateCandidate(
        string fullPath,
        string relativePath,
        out CodebaseIndexCandidate? candidate)
    {
        candidate = null;
        string extension = Path.GetExtension(relativePath);
        if (BinaryExtensions.Contains(extension))
        {
            return false;
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(fullPath);
        }
        catch (Exception exception) when (IsFileSystemAccessException(exception))
        {
            return false;
        }

        if (fileInfo.Length <= 0 ||
            fileInfo.Length > MaxIndexFileBytes)
        {
            return false;
        }

        candidate = new CodebaseIndexCandidate(
            fullPath,
            relativePath.Replace('\\', '/'),
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc);
        return true;
    }

    private static bool HasSameMetadata(
        CodebaseIndexCandidate candidate,
        CodebaseIndexedFileDocument indexedFile)
    {
        return candidate.Length == indexedFile.Length &&
            candidate.LastWriteTimeUtc == indexedFile.LastWriteTimeUtc;
    }

    private async Task<CodebaseIndexDocument?> LoadIndexAsync(
        string indexPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(indexPath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(indexPath);
            CodebaseIndexDocument? document = await JsonSerializer.DeserializeAsync(
                stream,
                CodebaseIndexJsonContext.Default.CodebaseIndexDocument,
                cancellationToken);
            return document is { Version: CurrentIndexVersion }
                ? document
                : null;
        }
        catch (Exception exception) when (exception is JsonException ||
                                          IsFileSystemAccessException(exception))
        {
            return null;
        }
    }

    private static async Task SaveIndexAsync(
        string indexPath,
        CodebaseIndexDocument index,
        CancellationToken cancellationToken)
    {
        string? parentDirectory = Path.GetDirectoryName(indexPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        await using FileStream stream = File.Create(indexPath);
        await JsonSerializer.SerializeAsync(
            stream,
            index,
            CodebaseIndexJsonContext.Default.CodebaseIndexDocument,
            cancellationToken);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }

    private static string[] ExtractSymbols(string content)
    {
        List<string> symbols = [];
        string[] lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("//", StringComparison.Ordinal) ||
                line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] tokens = ReadIdentifierTokens(line);
            if (tokens.Length == 0)
            {
                continue;
            }

            AddKeywordSymbol(tokens, symbols, "namespace");
            AddKeywordSymbol(tokens, symbols, "class");
            AddKeywordSymbol(tokens, symbols, "interface");
            AddKeywordSymbol(tokens, symbols, "record");
            AddKeywordSymbol(tokens, symbols, "struct");
            AddKeywordSymbol(tokens, symbols, "enum");
            AddKeywordSymbol(tokens, symbols, "function");
            AddKeywordSymbol(tokens, symbols, "def");

            if (TryGetCallableSymbol(line, tokens, out string? callableSymbol))
            {
                symbols.Add(callableSymbol!);
            }

            if (symbols.Count >= MaxSymbolsPerFile * 2)
            {
                break;
            }
        }

        return symbols.ToArray();
    }

    private static bool TryGetCallableSymbol(
        string line,
        string[] tokens,
        out string? symbol)
    {
        symbol = null;
        int openParenIndex = line.IndexOf('(', StringComparison.Ordinal);
        if (openParenIndex <= 0 ||
            tokens.Length == 0)
        {
            return false;
        }

        string firstToken = tokens[0].ToLowerInvariant();
        if (firstToken is "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "return" or "new")
        {
            return false;
        }

        string beforeOpenParen = line[..openParenIndex];
        string[] beforeTokens = ReadIdentifierTokens(beforeOpenParen);
        if (beforeTokens.Length == 0)
        {
            return false;
        }

        string candidate = beforeTokens[^1];
        if (candidate is "if" or "for" or "foreach" or "while" or "switch" or "catch")
        {
            return false;
        }

        symbol = candidate;
        return true;
    }

    private static void AddKeywordSymbol(
        string[] tokens,
        List<string> symbols,
        string keyword)
    {
        for (int index = 0; index < tokens.Length - 1; index++)
        {
            if (!string.Equals(tokens[index], keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            symbols.Add(tokens[index + 1]);
            return;
        }
    }

    private static void AddPathTerms(
        string relativePath,
        Dictionary<string, int> terms)
    {
        string withoutExtension = Path.ChangeExtension(relativePath, null) ?? relativePath;
        foreach (string token in Tokenize(withoutExtension))
        {
            AddTerm(token, terms, weight: 3);
        }
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        foreach (string token in ReadIdentifierTokens(value))
        {
            string normalized = token.ToLowerInvariant();
            if (normalized.Length < 2)
            {
                continue;
            }

            yield return normalized;
            foreach (string part in SplitCamelCase(token))
            {
                string normalizedPart = part.ToLowerInvariant();
                if (normalizedPart.Length >= 2 &&
                    !string.Equals(normalizedPart, normalized, StringComparison.Ordinal))
                {
                    yield return normalizedPart;
                }
            }
        }
    }

    private static string[] ReadIdentifierTokens(string value)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character) ||
                character == '_')
            {
                current.Append(character);
                continue;
            }

            FlushCurrent();
        }

        FlushCurrent();
        return tokens.ToArray();

        void FlushCurrent()
        {
            if (current.Length == 0)
            {
                return;
            }

            tokens.Add(current.ToString().Trim('_'));
            current.Clear();
        }
    }

    private static IEnumerable<string> SplitCamelCase(string value)
    {
        if (value.Length == 0)
        {
            yield break;
        }

        int start = 0;
        for (int index = 1; index < value.Length; index++)
        {
            if (!char.IsUpper(value[index]) ||
                !char.IsLower(value[index - 1]))
            {
                continue;
            }

            yield return value[start..index];
            start = index;
        }

        yield return value[start..];
    }

    private static void AddTerm(
        string token,
        Dictionary<string, int> terms,
        int weight = 1)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        string normalized = token.Trim().ToLowerInvariant();
        if (normalized.Length < 2)
        {
            return;
        }

        if (!terms.ContainsKey(normalized) &&
            terms.Count >= MaxTermsPerFile)
        {
            return;
        }

        terms[normalized] = terms.TryGetValue(normalized, out int count)
            ? count + weight
            : weight;
    }

    private static string ComputeSha256(string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static int CountLines(string content)
    {
        if (content.Length == 0)
        {
            return 0;
        }

        int count = 1;
        foreach (char character in content)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static string GetLanguage(string relativePath)
    {
        return Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".csproj" => "msbuild",
            ".css" => "css",
            ".fs" => "fsharp",
            ".go" => "go",
            ".html" => "html",
            ".java" => "java",
            ".js" => "javascript",
            ".json" => "json",
            ".jsx" => "javascript",
            ".md" => "markdown",
            ".ps1" => "powershell",
            ".py" => "python",
            ".rs" => "rust",
            ".sh" => "shell",
            ".sln" or ".slnx" => "solution",
            ".sql" => "sql",
            ".ts" => "typescript",
            ".tsx" => "typescript",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            _ => "text"
        };
    }

    private static bool IsDefaultIgnoredPath(
        string relativePath,
        bool isDirectory)
    {
        string normalizedPath = relativePath
            .Replace('\\', '/')
            .Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        if (DefaultIgnoredPathPrefixes.Any(prefix =>
                normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!isDirectory)
        {
            return false;
        }

        string name = Path.GetFileName(normalizedPath);
        return DefaultIgnoredDirectoryNames.Contains(name);
    }

    private static bool IsFileSystemAccessException(Exception exception)
    {
        return exception is UnauthorizedAccessException or
            IOException or
            PathTooLongException or
            System.Security.SecurityException;
    }

    private string GetWorkspaceRoot()
    {
        return Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
    }

    private static string GetIndexPath(string workspaceRoot)
    {
        return Path.Combine(
            workspaceRoot,
            ".nanoagent",
            "cache",
            "codebase-index.json");
    }

    private static string ToWorkspaceRelativePath(
        string workspaceRoot,
        string path)
    {
        return WorkspacePath.ToRelativePath(workspaceRoot, path);
    }

    private static string Truncate(
        string value,
        int maxCharacters)
    {
        string normalized = value.Trim();
        return normalized.Length <= maxCharacters
            ? normalized
            : normalized[..Math.Max(0, maxCharacters - 3)].TrimEnd() + "...";
    }

    private sealed record WorkspaceScan(
        IReadOnlyList<CodebaseIndexCandidate> Files,
        int SkippedFileCount);

    private sealed record CodebaseIndexCandidate(
        string FullPath,
        string RelativePath,
        long Length,
        DateTimeOffset LastWriteTimeUtc);
}

internal sealed class CodebaseIndexDocument
{
    public int Version { get; set; }

    public DateTimeOffset BuiltAtUtc { get; set; }

    public string WorkspaceRoot { get; set; } = string.Empty;

    public List<CodebaseIndexedFileDocument> Files { get; set; } = [];
}

internal sealed class CodebaseIndexedFileDocument
{
    public string Path { get; set; } = string.Empty;

    public long Length { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string Language { get; set; } = "text";

    public int LineCount { get; set; }

    public string[] Symbols { get; set; } = [];

    public Dictionary<string, int> Terms { get; set; } = new(StringComparer.Ordinal);
}
