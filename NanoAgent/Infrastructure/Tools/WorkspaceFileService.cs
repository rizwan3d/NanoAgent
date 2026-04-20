using System.Text;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class WorkspaceFileService : IWorkspaceFileService
{
    private const int MaxDirectoryEntries = 200;
    private const int MaxFileReadBytes = 262_144;
    private const int MaxSearchFileBytes = 262_144;
    private const int MaxSearchResults = 100;
    private const int FileWritePreviewContextLines = 1;
    private const int MaxFileWritePreviewLines = 8;

    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public WorkspaceFileService(IWorkspaceRootProvider workspaceRootProvider)
    {
        _workspaceRootProvider = workspaceRootProvider;
    }

    public Task<WorkspaceDirectoryListResult> ListDirectoryAsync(
        string? path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveWorkspacePath(path, directoryRequired: true, fileRequired: false);

        IEnumerable<string> entries = recursive
            ? Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.AllDirectories)
            : Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.TopDirectoryOnly);

        WorkspaceDirectoryEntry[] results = entries
            .OrderBy(static entry => entry, StringComparer.Ordinal)
            .Take(MaxDirectoryEntries)
            .Select(entry => new WorkspaceDirectoryEntry(
                ToWorkspaceRelativePath(entry),
                Directory.Exists(entry) ? "directory" : "file"))
            .ToArray();

        return Task.FromResult(new WorkspaceDirectoryListResult(
            ToWorkspaceRelativePath(fullPath),
            results));
    }

    public async Task<WorkspaceFileReadResult> ReadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveWorkspacePath(path, directoryRequired: false, fileRequired: true);
        FileInfo fileInfo = new(fullPath);
        if (fileInfo.Length > MaxFileReadBytes)
        {
            throw new InvalidOperationException(
                $"File '{ToWorkspaceRelativePath(fullPath)}' exceeds the maximum readable size of {MaxFileReadBytes} bytes.");
        }

        string content = await File.ReadAllTextAsync(
            fullPath,
            Encoding.UTF8,
            cancellationToken);

        return new WorkspaceFileReadResult(
            ToWorkspaceRelativePath(fullPath),
            content,
            content.Length);
    }

    public async Task<WorkspaceTextSearchResult> SearchTextAsync(
        WorkspaceTextSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveWorkspacePath(request.Path, directoryRequired: false, fileRequired: false);
        List<string> filesToSearch = [];

        if (File.Exists(fullPath))
        {
            filesToSearch.Add(fullPath);
        }
        else if (Directory.Exists(fullPath))
        {
            filesToSearch.AddRange(Directory.EnumerateFiles(
                fullPath,
                "*",
                SearchOption.AllDirectories));
        }
        else
        {
            throw new FileNotFoundException(
                $"Search path '{request.Path ?? "."}' does not exist.");
        }

        List<WorkspaceTextSearchMatch> matches = [];
        StringComparison comparison = request.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        foreach (string filePath in filesToSearch.OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo fileInfo = new(filePath);
            if (fileInfo.Length > MaxSearchFileBytes)
            {
                continue;
            }

            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(
                    filePath,
                    Encoding.UTF8,
                    cancellationToken);
            }
            catch (DecoderFallbackException)
            {
                continue;
            }
            catch (InvalidDataException)
            {
                continue;
            }

            for (int index = 0; index < lines.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!lines[index].Contains(request.Query, comparison))
                {
                    continue;
                }

                matches.Add(new WorkspaceTextSearchMatch(
                    ToWorkspaceRelativePath(filePath),
                    index + 1,
                    lines[index].Trim()));

                if (matches.Count >= MaxSearchResults)
                {
                    return new WorkspaceTextSearchResult(
                        request.Query,
                        ToWorkspaceRelativePath(fullPath),
                        matches);
                }
            }
        }

        return new WorkspaceTextSearchResult(
            request.Query,
            ToWorkspaceRelativePath(fullPath),
            matches);
    }

    public async Task<WorkspaceFileWriteResult> WriteFileAsync(
        string path,
        string content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                "File content must not be empty.");
        }

        string fullPath = ResolveWorkspacePath(path, directoryRequired: false, fileRequired: false);
        bool fileExists = File.Exists(fullPath);
        string? previousContent = null;
        if (fileExists && !overwrite)
        {
            throw new InvalidOperationException(
                $"File '{ToWorkspaceRelativePath(fullPath)}' already exists and overwrite is disabled.");
        }

        if (fileExists)
        {
            previousContent = await File.ReadAllTextAsync(
                fullPath,
                Encoding.UTF8,
                cancellationToken);
        }

        string? directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await File.WriteAllTextAsync(
            fullPath,
            content,
            Encoding.UTF8,
            cancellationToken);

        FileWritePreview preview = BuildFileWritePreview(previousContent, content);

        return new WorkspaceFileWriteResult(
            ToWorkspaceRelativePath(fullPath),
            fileExists,
            content.Length,
            preview.AddedLineCount,
            preview.RemovedLineCount,
            preview.Lines,
            preview.RemainingPreviewLineCount);
    }

    private string ResolveWorkspacePath(
        string? requestedPath,
        bool directoryRequired,
        bool fileRequired)
    {
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        string normalizedRequestedPath = string.IsNullOrWhiteSpace(requestedPath)
            ? workspaceRoot
            : requestedPath.Trim();

        string fullPath = Path.GetFullPath(
            Path.IsPathRooted(normalizedRequestedPath)
                ? normalizedRequestedPath
                : Path.Combine(workspaceRoot, normalizedRequestedPath));

        EnsureWithinWorkspace(workspaceRoot, fullPath);

        if (directoryRequired && !Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Directory '{ToWorkspaceRelativePath(fullPath)}' does not exist.");
        }

        if (fileRequired && !File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"File '{ToWorkspaceRelativePath(fullPath)}' does not exist.");
        }

        return fullPath;
    }

    private string ToWorkspaceRelativePath(string fullPath)
    {
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        if (string.Equals(workspaceRoot, fullPath, GetPathComparison()))
        {
            return ".";
        }

        return Path.GetRelativePath(workspaceRoot, fullPath)
            .Replace('\\', '/');
    }

    private static void EnsureWithinWorkspace(
        string workspaceRoot,
        string candidatePath)
    {
        string normalizedRoot = EnsureTrailingSeparator(workspaceRoot);
        string normalizedCandidate = EnsureTrailingSeparator(candidatePath);

        if (!normalizedCandidate.StartsWith(
                normalizedRoot,
                GetPathComparison()) &&
            !string.Equals(workspaceRoot, candidatePath, GetPathComparison()))
        {
            throw new InvalidOperationException(
                "Tool paths must stay within the current workspace.");
        }
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ||
               path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static FileWritePreview BuildFileWritePreview(
        string? previousContent,
        string currentContent)
    {
        string[] previousLines = SplitLines(previousContent);
        string[] currentLines = SplitLines(currentContent);
        IReadOnlyList<DiffLine> diffLines = previousContent is null
            ? currentLines
                .Select((line, index) => new DiffLine(
                    DiffLineKind.Addition,
                    null,
                    index + 1,
                    line))
                .ToArray()
            : ComputeDiff(previousLines, currentLines);

        int addedLineCount = diffLines.Count(static line => line.Kind == DiffLineKind.Addition);
        int removedLineCount = diffLines.Count(static line => line.Kind == DiffLineKind.Removal);

        if (diffLines.Count == 0)
        {
            return new FileWritePreview(0, 0, [], 0);
        }

        DiffLine[] previewDiffLines = SelectPreviewLines(diffLines)
            .Take(MaxFileWritePreviewLines)
            .ToArray();

        WorkspaceFileWritePreviewLine[] previewLines = previewDiffLines
            .Select(static line => new WorkspaceFileWritePreviewLine(
                line.LineNumber ?? 0,
                line.Kind switch
                {
                    DiffLineKind.Addition => "add",
                    DiffLineKind.Removal => "remove",
                    _ => "context"
                },
                line.Text))
            .ToArray();

        int remainingPreviewLineCount = Math.Max(
            0,
            SelectPreviewLines(diffLines).Count - previewLines.Length);

        return new FileWritePreview(
            addedLineCount,
            removedLineCount,
            previewLines,
            remainingPreviewLineCount);
    }

    private static string[] SplitLines(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        string[] rawLines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

        if (rawLines.Length > 0 && rawLines[^1].Length == 0)
        {
            return rawLines[..^1];
        }

        return rawLines;
    }

    private static IReadOnlyList<DiffLine> ComputeDiff(
        IReadOnlyList<string> previousLines,
        IReadOnlyList<string> currentLines)
    {
        if (previousLines.Count == 0 && currentLines.Count == 0)
        {
            return [];
        }

        int max = previousLines.Count + currentLines.Count;
        Dictionary<int, int> frontier = new() { [1] = 0 };
        List<Dictionary<int, int>> trace = new();

        for (int distance = 0; distance <= max; distance++)
        {
            trace.Add(new Dictionary<int, int>(frontier));

            for (int diagonal = -distance; diagonal <= distance; diagonal += 2)
            {
                int x;
                if (diagonal == -distance ||
                    (diagonal != distance &&
                     GetFrontierValue(frontier, diagonal - 1) < GetFrontierValue(frontier, diagonal + 1)))
                {
                    x = GetFrontierValue(frontier, diagonal + 1);
                }
                else
                {
                    x = GetFrontierValue(frontier, diagonal - 1) + 1;
                }

                int y = x - diagonal;

                while (x < previousLines.Count &&
                       y < currentLines.Count &&
                       string.Equals(previousLines[x], currentLines[y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                frontier[diagonal] = x;

                if (x >= previousLines.Count && y >= currentLines.Count)
                {
                    return Backtrack(trace, previousLines, currentLines);
                }
            }
        }

        return [];
    }

    private static IReadOnlyList<DiffLine> Backtrack(
        IReadOnlyList<Dictionary<int, int>> trace,
        IReadOnlyList<string> previousLines,
        IReadOnlyList<string> currentLines)
    {
        List<DiffLine> lines = [];
        int x = previousLines.Count;
        int y = currentLines.Count;

        for (int distance = trace.Count - 1; distance >= 0; distance--)
        {
            Dictionary<int, int> frontier = trace[distance];
            int diagonal = x - y;

            int previousDiagonal;
            if (diagonal == -distance ||
                (diagonal != distance &&
                 GetFrontierValue(frontier, diagonal - 1) < GetFrontierValue(frontier, diagonal + 1)))
            {
                previousDiagonal = diagonal + 1;
            }
            else
            {
                previousDiagonal = diagonal - 1;
            }

            int previousX = GetFrontierValue(frontier, previousDiagonal);
            int previousY = previousX - previousDiagonal;

            while (x > previousX && y > previousY)
            {
                lines.Add(new DiffLine(
                    DiffLineKind.Context,
                    x,
                    y,
                    previousLines[x - 1]));
                x--;
                y--;
            }

            if (distance == 0)
            {
                break;
            }

            if (x == previousX)
            {
                lines.Add(new DiffLine(
                    DiffLineKind.Addition,
                    null,
                    y,
                    currentLines[y - 1]));
                y--;
            }
            else
            {
                lines.Add(new DiffLine(
                    DiffLineKind.Removal,
                    x,
                    null,
                    previousLines[x - 1]));
                x--;
            }
        }

        lines.Reverse();
        return lines;
    }

    private static int GetFrontierValue(
        IReadOnlyDictionary<int, int> frontier,
        int diagonal)
    {
        return frontier.TryGetValue(diagonal, out int value)
            ? value
            : 0;
    }

    private static IReadOnlyList<DiffLine> SelectPreviewLines(
        IReadOnlyList<DiffLine> diffLines)
    {
        int firstChangedIndex = -1;
        for (int index = 0; index < diffLines.Count; index++)
        {
            if (diffLines[index].Kind != DiffLineKind.Context)
            {
                firstChangedIndex = index;
                break;
            }
        }

        if (firstChangedIndex < 0)
        {
            return [];
        }

        int start = Math.Max(0, firstChangedIndex - FileWritePreviewContextLines);
        int end = firstChangedIndex + 1;
        int trailingContextCount = 0;

        while (end < diffLines.Count)
        {
            if (diffLines[end].Kind == DiffLineKind.Context)
            {
                trailingContextCount++;
                if (trailingContextCount > FileWritePreviewContextLines)
                {
                    break;
                }
            }
            else
            {
                trailingContextCount = 0;
            }

            end++;
        }

        return diffLines
            .Skip(start)
            .Take(end - start)
            .ToArray();
    }

    private readonly record struct FileWritePreview(
        int AddedLineCount,
        int RemovedLineCount,
        WorkspaceFileWritePreviewLine[] Lines,
        int RemainingPreviewLineCount);

    private readonly record struct DiffLine(
        DiffLineKind Kind,
        int? OriginalLineNumber,
        int? UpdatedLineNumber,
        string Text)
    {
        public int? LineNumber => Kind == DiffLineKind.Removal
            ? OriginalLineNumber
            : UpdatedLineNumber;
    }

    private enum DiffLineKind
    {
        Context = 0,
        Addition = 1,
        Removal = 2
    }
}
