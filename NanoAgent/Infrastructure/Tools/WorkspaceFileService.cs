using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class WorkspaceFileService : IWorkspaceFileService
{
    private const int MaxDirectoryEntries = 200;
    private const int MaxFileReadBytes = 262_144;
    private const int MaxSearchFileBytes = 262_144;
    private const int MaxSearchResults = 100;
    private const int MaxFileSearchResults = 200;
    private const int FileWritePreviewContextLines = 1;
    private const int MaxFileWritePreviewLines = 8;

    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public WorkspaceFileService(IWorkspaceRootProvider workspaceRootProvider)
    {
        _workspaceRootProvider = workspaceRootProvider;
    }

    public async Task<WorkspaceApplyPatchResult> ApplyPatchAsync(
        string patch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PatchDocument document = ParsePatch(patch);
        return await ApplyPatchDocumentAsync(document, cancellationToken);
    }

    public async Task<WorkspaceApplyPatchExecutionResult> ApplyPatchWithTrackingAsync(
        string patch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PatchDocument document = ParsePatch(patch);
        string[] trackedPaths = GetTrackedPatchPaths(document);
        WorkspaceFileEditState[] beforeStates = await CaptureFileStatesAsync(
            trackedPaths,
            cancellationToken);
        WorkspaceApplyPatchResult result = await ApplyPatchDocumentAsync(document, cancellationToken);
        WorkspaceFileEditState[] afterStates = await CaptureFileStatesAsync(
            trackedPaths,
            cancellationToken);

        WorkspaceFileEditTransaction? editTransaction = trackedPaths.Length == 0
            ? null
            : new WorkspaceFileEditTransaction(
                $"apply_patch ({result.FileCount} {(result.FileCount == 1 ? "file" : "files")})",
                beforeStates,
                afterStates);

        return new WorkspaceApplyPatchExecutionResult(
            result,
            editTransaction);
    }

    public async Task ApplyFileEditStatesAsync(
        IReadOnlyList<WorkspaceFileEditState> states,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(states);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceFileEditState[] normalizedStates = states
            .Where(static state => state is not null)
            .GroupBy(static state => state.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .ToArray();

        foreach (WorkspaceFileEditState state in normalizedStates.Where(static state => state.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullPath = ResolveWorkspacePath(state.Path, directoryRequired: false, fileRequired: false);
            if (Directory.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"Cannot restore file '{state.Path}' because a directory exists at that path.");
            }

            EnsureParentDirectory(fullPath);
            await File.WriteAllTextAsync(
                fullPath,
                state.Content!,
                Encoding.UTF8,
                cancellationToken);
        }

        foreach (WorkspaceFileEditState state in normalizedStates.Where(static state => !state.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullPath = ResolveWorkspacePath(state.Path, directoryRequired: false, fileRequired: false);
            if (Directory.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"Cannot delete '{state.Path}' during rollback because a directory exists at that path.");
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
    }

    public async Task<WorkspaceFileWriteExecutionResult> WriteFileWithTrackingAsync(
        string path,
        string content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceFileEditState beforeState = await CaptureFileStateAsync(
            path,
            cancellationToken);
        WorkspaceFileWriteResult result = await WriteFileAsync(
            path,
            content,
            overwrite,
            cancellationToken);
        WorkspaceFileEditState afterState = await CaptureFileStateAsync(
            path,
            cancellationToken);

        return new WorkspaceFileWriteExecutionResult(
            result,
            new WorkspaceFileEditTransaction(
                $"file_write ({result.Path})",
                [beforeState],
                [afterState]));
    }

    private async Task<WorkspaceApplyPatchResult> ApplyPatchDocumentAsync(
        PatchDocument document,
        CancellationToken cancellationToken)
    {
        List<WorkspaceApplyPatchFileResult> files = [];

        foreach (PatchOperation operation in document.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WorkspaceApplyPatchFileResult result = operation.Kind switch
            {
                PatchOperationKind.Add => await ApplyAddFileOperationAsync(operation, cancellationToken),
                PatchOperationKind.Delete => await ApplyDeleteFileOperationAsync(operation, cancellationToken),
                PatchOperationKind.Update => await ApplyUpdateFileOperationAsync(operation, cancellationToken),
                _ => throw new InvalidOperationException("Unsupported patch operation.")
            };

            files.Add(result);
        }

        return new WorkspaceApplyPatchResult(
            files.Count,
            files.Sum(static file => file.AddedLineCount),
            files.Sum(static file => file.RemovedLineCount),
            files);
    }

    public async Task<WorkspaceDirectoryListResult> ListDirectoryAsync(
        string? path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveWorkspacePath(path, directoryRequired: true, fileRequired: false);
        return new WorkspaceDirectoryListResult(
            ToWorkspaceRelativePath(fullPath),
            ListDirectoryManaged(fullPath, recursive));
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

    public async Task<WorkspaceFileSearchResult> SearchFilesAsync(
        WorkspaceFileSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveWorkspacePath(request.Path, directoryRequired: false, fileRequired: false);
        return new WorkspaceFileSearchResult(
            request.Query,
            ToWorkspaceRelativePath(fullPath),
            SearchFilesManaged(request, fullPath));
    }

    public async Task<WorkspaceTextSearchResult> SearchTextAsync(
        WorkspaceTextSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveWorkspacePath(request.Path, directoryRequired: false, fileRequired: false);
        return new WorkspaceTextSearchResult(
            request.Query,
            ToWorkspaceRelativePath(fullPath),
            await SearchTextManagedAsync(fullPath, request, cancellationToken));
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

        EnsureParentDirectory(fullPath);

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

    private async Task<WorkspaceFileEditState[]> CaptureFileStatesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        List<WorkspaceFileEditState> states = [];

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            states.Add(await CaptureFileStateAsync(path, cancellationToken));
        }

        return states.ToArray();
    }

    private async Task<WorkspaceFileEditState> CaptureFileStateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string fullPath = ResolveWorkspacePath(path, directoryRequired: false, fileRequired: false);
        if (Directory.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"Cannot track '{path}' for undo/redo because it resolves to a directory.");
        }

        if (!File.Exists(fullPath))
        {
            return new WorkspaceFileEditState(
                ToWorkspaceRelativePath(fullPath),
                exists: false,
                content: null);
        }

        string content = await File.ReadAllTextAsync(
            fullPath,
            Encoding.UTF8,
            cancellationToken);

        return new WorkspaceFileEditState(
            ToWorkspaceRelativePath(fullPath),
            exists: true,
            content);
    }

    private async Task<WorkspaceApplyPatchFileResult> ApplyAddFileOperationAsync(
        PatchOperation operation,
        CancellationToken cancellationToken)
    {
        string fullPath = ResolveWorkspacePath(operation.Path, directoryRequired: false, fileRequired: false);
        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"Cannot add '{operation.Path}' because the file already exists.");
        }

        string content = JoinLines(operation.AddLines, trailingNewLine: false);
        EnsureParentDirectory(fullPath);
        await File.WriteAllTextAsync(
            fullPath,
            content,
            Encoding.UTF8,
            cancellationToken);

        return CreatePatchFileResult(
            fullPath,
            previousPath: null,
            "add",
            previousContent: null,
            currentContent: content);
    }

    private async Task<WorkspaceApplyPatchFileResult> ApplyDeleteFileOperationAsync(
        PatchOperation operation,
        CancellationToken cancellationToken)
    {
        string fullPath = ResolveWorkspacePath(operation.Path, directoryRequired: false, fileRequired: true);
        string previousContent = await File.ReadAllTextAsync(
            fullPath,
            Encoding.UTF8,
            cancellationToken);

        File.Delete(fullPath);

        return CreatePatchFileResult(
            fullPath,
            previousPath: null,
            "delete",
            previousContent,
            string.Empty);
    }

    private async Task<WorkspaceApplyPatchFileResult> ApplyUpdateFileOperationAsync(
        PatchOperation operation,
        CancellationToken cancellationToken)
    {
        string currentFullPath = ResolveWorkspacePath(operation.Path, directoryRequired: false, fileRequired: true);
        string previousContent = await File.ReadAllTextAsync(
            currentFullPath,
            Encoding.UTF8,
            cancellationToken);
        string updatedContent = ApplyUpdatePatch(operation.Path, previousContent, operation.Hunks);

        string destinationFullPath = operation.MoveToPath is null
            ? currentFullPath
            : ResolveWorkspacePath(operation.MoveToPath, directoryRequired: false, fileRequired: false);

        if (!string.Equals(currentFullPath, destinationFullPath, GetPathComparison()) &&
            File.Exists(destinationFullPath))
        {
            throw new InvalidOperationException(
                $"Cannot move '{operation.Path}' to '{operation.MoveToPath}' because the destination already exists.");
        }

        EnsureParentDirectory(destinationFullPath);

        await File.WriteAllTextAsync(
            destinationFullPath,
            updatedContent,
            Encoding.UTF8,
            cancellationToken);

        if (!string.Equals(currentFullPath, destinationFullPath, GetPathComparison()) &&
            File.Exists(currentFullPath))
        {
            File.Delete(currentFullPath);
        }

        return CreatePatchFileResult(
            destinationFullPath,
            operation.MoveToPath is null
                ? null
                : ToWorkspaceRelativePath(currentFullPath),
            operation.MoveToPath is null ? "update" : "move",
            previousContent,
            updatedContent);
    }

    private WorkspaceApplyPatchFileResult CreatePatchFileResult(
        string fullPath,
        string? previousPath,
        string operation,
        string? previousContent,
        string currentContent)
    {
        FileWritePreview preview = BuildFileWritePreview(previousContent, currentContent);

        return new WorkspaceApplyPatchFileResult(
            ToWorkspaceRelativePath(fullPath),
            operation,
            previousPath,
            preview.AddedLineCount,
            preview.RemovedLineCount,
            preview.Lines,
            preview.RemainingPreviewLineCount);
    }

    private WorkspaceDirectoryEntry[] ListDirectoryManaged(
        string fullPath,
        bool recursive)
    {
        IEnumerable<string> entries = recursive
            ? Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.AllDirectories)
            : Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.TopDirectoryOnly);

        return entries
            .OrderBy(static entry => entry, StringComparer.Ordinal)
            .Take(MaxDirectoryEntries)
            .Select(entry => new WorkspaceDirectoryEntry(
                ToWorkspaceRelativePath(entry),
                Directory.Exists(entry) ? "directory" : "file"))
            .ToArray();
    }

    private IReadOnlyList<string> SearchFilesManaged(
        WorkspaceFileSearchRequest request,
        string fullPath)
    {
        StringComparison comparison = request.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        IEnumerable<string> files = File.Exists(fullPath)
            ? [fullPath]
            : Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories);

        return files
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(filePath => ToWorkspaceRelativePath(filePath))
            .Where(relativePath => relativePath.Contains(request.Query, comparison))
            .Take(MaxFileSearchResults)
            .ToArray();
    }

    private async Task<IReadOnlyList<WorkspaceTextSearchMatch>> SearchTextManagedAsync(
        string fullPath,
        WorkspaceTextSearchRequest request,
        CancellationToken cancellationToken)
    {
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
                    return matches;
                }
            }
        }

        return matches;
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

    private static void EnsureParentDirectory(string fullPath)
    {
        string? directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    private static PatchDocument ParsePatch(string patch)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            throw new FormatException("Patch text must not be empty.");
        }

        string[] lines = patch
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

        int lineIndex = 0;
        SkipEmptyLines(lines, ref lineIndex);

        if (lineIndex >= lines.Length || !string.Equals(lines[lineIndex], "*** Begin Patch", StringComparison.Ordinal))
        {
            throw new FormatException("Patch text must begin with '*** Begin Patch'.");
        }

        lineIndex++;
        List<PatchOperation> operations = [];

        while (lineIndex < lines.Length)
        {
            string currentLine = lines[lineIndex];
            if (string.Equals(currentLine, "*** End Patch", StringComparison.Ordinal))
            {
                return new PatchDocument(operations);
            }

            if (string.IsNullOrWhiteSpace(currentLine))
            {
                lineIndex++;
                continue;
            }

            if (currentLine.StartsWith("*** Add File: ", StringComparison.Ordinal))
            {
                operations.Add(ParseAddFile(lines, ref lineIndex));
                continue;
            }

            if (currentLine.StartsWith("*** Delete File: ", StringComparison.Ordinal))
            {
                operations.Add(ParseDeleteFile(lines, ref lineIndex));
                continue;
            }

            if (currentLine.StartsWith("*** Update File: ", StringComparison.Ordinal))
            {
                operations.Add(ParseUpdateFile(lines, ref lineIndex));
                continue;
            }

            throw new FormatException($"Unrecognized patch line: '{currentLine}'.");
        }

        throw new FormatException("Patch text must end with '*** End Patch'.");
    }

    private static PatchOperation ParseAddFile(
        IReadOnlyList<string> lines,
        ref int lineIndex)
    {
        string path = ParseHeaderValue(lines[lineIndex], "*** Add File: ");
        lineIndex++;

        List<string> fileLines = [];
        while (lineIndex < lines.Count &&
               !lines[lineIndex].StartsWith("*** ", StringComparison.Ordinal))
        {
            if (lines[lineIndex].Length == 0)
            {
                throw new FormatException("Add file patch lines must start with '+'.");
            }

            if (lines[lineIndex][0] != '+')
            {
                throw new FormatException("Add file patch lines must start with '+'.");
            }

            fileLines.Add(lines[lineIndex][1..]);
            lineIndex++;
        }

        if (fileLines.Count == 0)
        {
            throw new FormatException("Add file operations must include at least one '+' line.");
        }

        return new PatchOperation(
            PatchOperationKind.Add,
            path,
            MoveToPath: null,
            fileLines,
            []);
    }

    private static PatchOperation ParseDeleteFile(
        IReadOnlyList<string> lines,
        ref int lineIndex)
    {
        string path = ParseHeaderValue(lines[lineIndex], "*** Delete File: ");
        lineIndex++;

        return new PatchOperation(
            PatchOperationKind.Delete,
            path,
            MoveToPath: null,
            [],
            []);
    }

    private static PatchOperation ParseUpdateFile(
        IReadOnlyList<string> lines,
        ref int lineIndex)
    {
        string path = ParseHeaderValue(lines[lineIndex], "*** Update File: ");
        lineIndex++;

        string? moveToPath = null;
        if (lineIndex < lines.Count &&
            lines[lineIndex].StartsWith("*** Move to: ", StringComparison.Ordinal))
        {
            moveToPath = ParseHeaderValue(lines[lineIndex], "*** Move to: ");
            lineIndex++;
        }

        List<PatchHunk> hunks = [];
        List<PatchLine>? currentHunkLines = null;

        while (lineIndex < lines.Count &&
               !lines[lineIndex].StartsWith("*** ", StringComparison.Ordinal))
        {
            string line = lines[lineIndex];
            if (string.Equals(line, "\\ No newline at end of file", StringComparison.Ordinal) ||
                string.Equals(line, "*** End of File", StringComparison.Ordinal))
            {
                lineIndex++;
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (currentHunkLines is not null)
                {
                    hunks.Add(new PatchHunk(currentHunkLines));
                }

                currentHunkLines = [];
                lineIndex++;
                continue;
            }

            if (line.Length == 0 || line[0] is not (' ' or '+' or '-'))
            {
                throw new FormatException($"Invalid update patch line: '{line}'.");
            }

            currentHunkLines ??= [];
            currentHunkLines.Add(new PatchLine(
                line[0] switch
                {
                    ' ' => PatchLineKind.Context,
                    '+' => PatchLineKind.Addition,
                    '-' => PatchLineKind.Removal,
                    _ => throw new FormatException($"Invalid patch line prefix in '{line}'.")
                },
                line[1..]));

            lineIndex++;
        }

        if (currentHunkLines is not null)
        {
            hunks.Add(new PatchHunk(currentHunkLines));
        }

        if (moveToPath is null && hunks.Count == 0)
        {
            throw new FormatException("Update file operations must include at least one hunk or a move target.");
        }

        return new PatchOperation(
            PatchOperationKind.Update,
            path,
            moveToPath,
            [],
            hunks);
    }

    private static void SkipEmptyLines(
        IReadOnlyList<string> lines,
        ref int lineIndex)
    {
        while (lineIndex < lines.Count &&
               string.IsNullOrWhiteSpace(lines[lineIndex]))
        {
            lineIndex++;
        }
    }

    private static string ParseHeaderValue(
        string line,
        string prefix)
    {
        string value = line[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"Patch header '{prefix.Trim()}' must include a path.");
        }

        return value;
    }

    private static string ApplyUpdatePatch(
        string path,
        string previousContent,
        IReadOnlyList<PatchHunk> hunks)
    {
        List<string> currentLines = SplitLines(previousContent).ToList();
        int searchStart = 0;

        foreach (PatchHunk hunk in hunks)
        {
            string[] beforeLines = hunk.Lines
                .Where(static line => line.Kind is PatchLineKind.Context or PatchLineKind.Removal)
                .Select(static line => line.Text)
                .ToArray();
            string[] afterLines = hunk.Lines
                .Where(static line => line.Kind is PatchLineKind.Context or PatchLineKind.Addition)
                .Select(static line => line.Text)
                .ToArray();

            int matchIndex = beforeLines.Length == 0
                ? searchStart
                : FindSequence(currentLines, beforeLines, searchStart);

            if (matchIndex < 0 && beforeLines.Length > 0 && searchStart > 0)
            {
                matchIndex = FindSequence(currentLines, beforeLines, 0);
            }

            if (matchIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Could not apply the requested patch because the target context was not found in '{path}'.");
            }

            currentLines.RemoveRange(matchIndex, beforeLines.Length);
            currentLines.InsertRange(matchIndex, afterLines);
            searchStart = matchIndex + afterLines.Length;
        }

        bool trailingNewLine = previousContent.EndsWith('\n') || previousContent.EndsWith('\r');
        return JoinLines(currentLines, trailingNewLine);
    }

    private static int FindSequence(
        IReadOnlyList<string> source,
        IReadOnlyList<string> target,
        int startIndex)
    {
        if (target.Count == 0)
        {
            return startIndex;
        }

        for (int index = Math.Max(0, startIndex); index <= source.Count - target.Count; index++)
        {
            bool matched = true;
            for (int offset = 0; offset < target.Count; offset++)
            {
                if (!string.Equals(source[index + offset], target[offset], StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return index;
            }
        }

        return -1;
    }

    private static string JoinLines(
        IEnumerable<string> lines,
        bool trailingNewLine)
    {
        string content = string.Join("\n", lines);
        return trailingNewLine && content.Length > 0
            ? content + "\n"
            : content;
    }

    private static string[] GetTrackedPatchPaths(PatchDocument document)
    {
        return document.Operations
            .SelectMany(static operation => operation.MoveToPath is null
                ? [operation.Path]
                : new[] { operation.Path, operation.MoveToPath })
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FileWritePreview BuildFileWritePreview(
        string? previousContent,
        string currentContent)
    {
        string[] currentLines = SplitLines(currentContent);
        IReadOnlyList<PreviewDiffLine> diffLines = previousContent is null
            ? currentLines
                .Select((line, index) => new PreviewDiffLine(
                    ChangeType.Inserted,
                    index + 1,
                    line))
                .ToArray()
            : CreatePreviewDiffLines(previousContent, currentContent);

        int addedLineCount = diffLines.Count(static line => line.Kind == ChangeType.Inserted);
        int removedLineCount = diffLines.Count(static line => line.Kind == ChangeType.Deleted);

        if (diffLines.Count == 0)
        {
            return new FileWritePreview(0, 0, [], 0);
        }

        IReadOnlyList<PreviewDiffLine> selectedPreviewLines = SelectPreviewLines(diffLines);
        PreviewDiffLine[] previewDiffLines = selectedPreviewLines
            .Take(MaxFileWritePreviewLines)
            .ToArray();

        WorkspaceFileWritePreviewLine[] previewLines = previewDiffLines
            .Select(static line => new WorkspaceFileWritePreviewLine(
                line.LineNumber,
                line.Kind switch
                {
                    ChangeType.Inserted => "add",
                    ChangeType.Deleted => "remove",
                    _ => "context"
                },
                line.Text))
            .ToArray();

        int remainingPreviewLineCount = Math.Max(
            0,
            selectedPreviewLines.Count - previewLines.Length);

        return new FileWritePreview(
            addedLineCount,
            removedLineCount,
            previewLines,
            remainingPreviewLineCount);
    }

    private static IReadOnlyList<PreviewDiffLine> CreatePreviewDiffLines(
        string previousContent,
        string currentContent)
    {
        DiffPaneModel diff = InlineDiffBuilder.Diff(previousContent, currentContent);
        List<PreviewDiffLine> lines = new(diff.Lines.Count);
        int oldLineNumber = 0;
        int newLineNumber = 0;

        foreach (DiffPiece piece in diff.Lines)
        {
            switch (piece.Type)
            {
                case ChangeType.Deleted:
                    oldLineNumber++;
                    lines.Add(new PreviewDiffLine(piece.Type, oldLineNumber, piece.Text));
                    break;

                case ChangeType.Inserted:
                    newLineNumber++;
                    lines.Add(new PreviewDiffLine(piece.Type, newLineNumber, piece.Text));
                    break;

                default:
                    oldLineNumber++;
                    newLineNumber++;
                    lines.Add(new PreviewDiffLine(piece.Type, newLineNumber, piece.Text));
                    break;
            }
        }

        return lines;
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

    private static IReadOnlyList<PreviewDiffLine> SelectPreviewLines(
        IReadOnlyList<PreviewDiffLine> diffLines)
    {
        int firstChangedIndex = -1;
        for (int index = 0; index < diffLines.Count; index++)
        {
            if (diffLines[index].Kind is ChangeType.Inserted or ChangeType.Deleted)
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
            if (diffLines[end].Kind is not (ChangeType.Inserted or ChangeType.Deleted))
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

    private readonly record struct PatchDocument(
        IReadOnlyList<PatchOperation> Operations);

    private readonly record struct PatchOperation(
        PatchOperationKind Kind,
        string Path,
        string? MoveToPath,
        IReadOnlyList<string> AddLines,
        IReadOnlyList<PatchHunk> Hunks);

    private readonly record struct PatchHunk(
        IReadOnlyList<PatchLine> Lines);

    private readonly record struct PatchLine(
        PatchLineKind Kind,
        string Text);

    private enum PatchOperationKind
    {
        Add = 0,
        Delete = 1,
        Update = 2
    }

    private enum PatchLineKind
    {
        Context = 0,
        Addition = 1,
        Removal = 2
    }

    private readonly record struct FileWritePreview(
        int AddedLineCount,
        int RemovedLineCount,
        WorkspaceFileWritePreviewLine[] Lines,
        int RemainingPreviewLineCount);

    private readonly record struct PreviewDiffLine(
        ChangeType Kind,
        int LineNumber,
        string Text);
}
