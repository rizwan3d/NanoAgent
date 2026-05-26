using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Workspaces;
using System.Runtime.ExceptionServices;
using System.Text;

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

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

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
        WorkspaceApplyPatchResult result;
        try
        {
            result = await ApplyPatchDocumentAsync(document, cancellationToken);
        }
        catch (Exception exception)
        {
            try
            {
                await ApplyFileEditStatesAsync(beforeStates, CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "Patch application failed and rollback did not complete successfully.",
                    exception,
                    rollbackException);
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }

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

        StringComparer pathComparer = WorkspacePath.GetPathComparer();
        WorkspaceFileEditState[] normalizedStates = states
            .Where(static state => state is not null)
            .GroupBy(static state => state.Path, pathComparer)
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
                Utf8NoBom,
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

    public async Task<WorkspaceFileDeleteExecutionResult> DeleteFileWithTrackingAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceFileEditState beforeState = await CaptureFileStateAsync(
            path,
            cancellationToken);
        WorkspaceFileDeleteResult result = await DeleteFileAsync(
            path,
            cancellationToken);
        WorkspaceFileEditState afterState = await CaptureFileStateAsync(
            path,
            cancellationToken);

        return new WorkspaceFileDeleteExecutionResult(
            result,
            new WorkspaceFileEditTransaction(
                $"file_delete ({result.Path})",
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
        WorkspaceIgnoreMatcher ignoreMatcher = LoadWorkspaceIgnoreMatcher();
        EnsurePathNotIgnored(fullPath, isDirectory: true, ignoreMatcher);
        return new WorkspaceDirectoryListResult(
            ToWorkspaceRelativePath(fullPath),
            ListDirectoryManaged(fullPath, recursive, ignoreMatcher));
    }

    public async Task<WorkspaceFileReadResult> ReadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveWorkspacePath(path, directoryRequired: false, fileRequired: true);
        EnsurePathNotIgnored(fullPath, isDirectory: false, LoadWorkspaceIgnoreMatcher());
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

    public async Task<WorkspaceFileDeleteResult> DeleteFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveWorkspacePath(path, directoryRequired: false, fileRequired: true);
        EnsurePathNotIgnored(fullPath, isDirectory: false, LoadWorkspaceIgnoreMatcher());
        string previousContent = await File.ReadAllTextAsync(
            fullPath,
            Encoding.UTF8,
            cancellationToken);
        FileWritePreview preview = BuildFileWritePreview(previousContent, string.Empty);

        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(fullPath);

        return new WorkspaceFileDeleteResult(
            ToWorkspaceRelativePath(fullPath),
            previousContent.Length,
            preview.AddedLineCount,
            preview.RemovedLineCount,
            preview.Lines,
            preview.RemainingPreviewLineCount);
    }

    public async Task<WorkspaceFileSearchResult> SearchFilesAsync(
        WorkspaceFileSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveWorkspacePath(request.Path, directoryRequired: false, fileRequired: false);
        WorkspaceIgnoreMatcher ignoreMatcher = LoadWorkspaceIgnoreMatcher();
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            EnsurePathNotIgnored(fullPath, Directory.Exists(fullPath), ignoreMatcher);
        }

        return new WorkspaceFileSearchResult(
            request.Query,
            ToWorkspaceRelativePath(fullPath),
            SearchFilesManaged(request, fullPath, ignoreMatcher));
    }

    public async Task<WorkspaceTextSearchResult> SearchTextAsync(
        WorkspaceTextSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveWorkspacePath(request.Path, directoryRequired: false, fileRequired: false);
        WorkspaceIgnoreMatcher ignoreMatcher = LoadWorkspaceIgnoreMatcher();
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            EnsurePathNotIgnored(fullPath, Directory.Exists(fullPath), ignoreMatcher);
        }

        return new WorkspaceTextSearchResult(
            request.Query,
            ToWorkspaceRelativePath(fullPath),
            await SearchTextManagedAsync(fullPath, request, ignoreMatcher, cancellationToken));
    }

    public async Task<WorkspaceFileWriteResult> WriteFileAsync(
        string path,
        string content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(content);

        string fullPath = ResolveWorkspacePath(path, directoryRequired: false, fileRequired: false);
        EnsurePathNotIgnored(fullPath, isDirectory: Directory.Exists(fullPath), LoadWorkspaceIgnoreMatcher());
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
            Utf8NoBom,
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
        EnsurePathNotIgnored(fullPath, isDirectory: Directory.Exists(fullPath), LoadWorkspaceIgnoreMatcher());
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
        EnsurePathNotIgnored(fullPath, isDirectory: Directory.Exists(fullPath), LoadWorkspaceIgnoreMatcher());
        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"Cannot add '{operation.Path}' because the file already exists.");
        }

        string content = operation.AddLines.Count == 0
            ? string.Empty
            : JoinLines(operation.AddLines, trailingNewLine: true);
        EnsureParentDirectory(fullPath);
        await File.WriteAllTextAsync(
            fullPath,
            content,
            Utf8NoBom,
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
        EnsurePathNotIgnored(fullPath, isDirectory: false, LoadWorkspaceIgnoreMatcher());
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
        WorkspaceIgnoreMatcher ignoreMatcher = LoadWorkspaceIgnoreMatcher();
        EnsurePathNotIgnored(currentFullPath, isDirectory: false, ignoreMatcher);
        string previousContent = await File.ReadAllTextAsync(
            currentFullPath,
            Encoding.UTF8,
            cancellationToken);
        bool isMoveOnly = operation.MoveToPath is not null && operation.Hunks.Count == 0;
        string updatedContent = isMoveOnly
            ? previousContent
            : ApplyUpdatePatch(operation.Path, previousContent, operation.Hunks);

        string destinationFullPath = operation.MoveToPath is null
            ? currentFullPath
            : ResolveWorkspacePath(operation.MoveToPath, directoryRequired: false, fileRequired: false);
        EnsurePathNotIgnored(destinationFullPath, Directory.Exists(destinationFullPath), ignoreMatcher);

        if (!WorkspacePath.PathEquals(currentFullPath, destinationFullPath) &&
            File.Exists(destinationFullPath))
        {
            throw new InvalidOperationException(
                $"Cannot move '{operation.Path}' to '{operation.MoveToPath}' because the destination already exists.");
        }

        EnsureParentDirectory(destinationFullPath);

        await File.WriteAllTextAsync(
            destinationFullPath,
            updatedContent,
            Utf8NoBom,
            cancellationToken);

        if (!WorkspacePath.PathEquals(currentFullPath, destinationFullPath) &&
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
        bool recursive,
        WorkspaceIgnoreMatcher ignoreMatcher)
    {
        IEnumerable<string> entries = EnumerateFileSystemEntriesSafely(
            fullPath,
            recursive,
            ignoreMatcher);

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
        string fullPath,
        WorkspaceIgnoreMatcher ignoreMatcher)
    {
        StringComparison comparison = request.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        IEnumerable<string> files = File.Exists(fullPath)
            ? [fullPath]
            : Directory.Exists(fullPath)
                ? EnumerateFilesSafely(fullPath, recursive: true, ignoreMatcher)
                : throw new FileNotFoundException(
                    $"Search path '{request.Path ?? "."}' does not exist.");

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
        WorkspaceIgnoreMatcher ignoreMatcher,
        CancellationToken cancellationToken)
    {
        List<string> filesToSearch = [];

        if (File.Exists(fullPath))
        {
            filesToSearch.Add(fullPath);
        }
        else if (Directory.Exists(fullPath))
        {
            filesToSearch.AddRange(EnumerateFilesSafely(
                fullPath,
                recursive: true,
                ignoreMatcher));
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

            if (!TryGetFileLength(filePath, out long fileLength) ||
                fileLength > MaxSearchFileBytes)
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
            catch (Exception exception) when (IsFileSystemAccessException(exception))
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

    private static IEnumerable<string> EnumerateFilesSafely(
        string root,
        bool recursive,
        WorkspaceIgnoreMatcher ignoreMatcher)
    {
        return EnumerateFileSystemEntriesSafely(root, recursive, ignoreMatcher)
            .Where(static entry => File.Exists(entry));
    }

    private static IEnumerable<string> EnumerateFileSystemEntriesSafely(
        string root,
        bool recursive,
        WorkspaceIgnoreMatcher ignoreMatcher)
    {
        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(root);

        while (pendingDirectories.Count > 0)
        {
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

            foreach (string entry in entries)
            {
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
                if (ignoreMatcher.IsIgnored(entry, isDirectory))
                {
                    continue;
                }

                yield return entry;

                if (recursive &&
                    isDirectory &&
                    !attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pendingDirectories.Push(entry);
                }
            }

            if (!recursive)
            {
                yield break;
            }
        }
    }

    private static bool TryGetFileLength(
        string path,
        out long length)
    {
        try
        {
            length = new FileInfo(path).Length;
            return true;
        }
        catch (Exception exception) when (IsFileSystemAccessException(exception))
        {
            length = 0;
            return false;
        }
    }

    private static bool IsFileSystemAccessException(Exception exception)
    {
        return exception is UnauthorizedAccessException or
            IOException or
            PathTooLongException or
            System.Security.SecurityException;
    }

    private string ResolveWorkspacePath(
        string? requestedPath,
        bool directoryRequired,
        bool fileRequired)
    {
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        string fullPath = WorkspacePath.Resolve(workspaceRoot, requestedPath);
        WorkspaceResolvedPath.EnsurePathStaysWithinWorkspace(workspaceRoot, fullPath);

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

    private WorkspaceIgnoreMatcher LoadWorkspaceIgnoreMatcher()
    {
        return WorkspaceIgnoreMatcher.Load(GetWorkspaceRoot());
    }

    private void EnsurePathNotIgnored(
        string fullPath,
        bool isDirectory,
        WorkspaceIgnoreMatcher ignoreMatcher)
    {
        if (!ignoreMatcher.IsIgnored(fullPath, isDirectory))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Path '{ToWorkspaceRelativePath(fullPath)}' is excluded by .nanoagent/.nanoignore.");
    }

    private string ToWorkspaceRelativePath(string fullPath)
    {
        return WorkspacePath.ToRelativePath(GetWorkspaceRoot(), fullPath);
    }

    private string GetWorkspaceRoot()
    {
        return Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
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

        string[] lines = StripPatchHeredoc(patch.Trim())
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

        int lineIndex = FindPatchMarker(lines, "*** Begin Patch");
        int endPatchIndex = FindPatchMarker(lines, "*** End Patch");

        if (lineIndex < 0 || endPatchIndex < 0 || lineIndex >= endPatchIndex)
        {
            throw new FormatException("Patch text must include '*** Begin Patch' before '*** End Patch'.");
        }

        lineIndex++;
        List<PatchOperation> operations = [];

        while (lineIndex < endPatchIndex)
        {
            string currentLine = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(currentLine))
            {
                lineIndex++;
                continue;
            }

            if (IsPatchHeader(currentLine, "*** Add File:"))
            {
                operations.Add(ParseAddFile(lines, ref lineIndex));
                continue;
            }

            if (IsPatchHeader(currentLine, "*** Delete File:"))
            {
                operations.Add(ParseDeleteFile(lines, ref lineIndex));
                continue;
            }

            if (IsPatchHeader(currentLine, "*** Update File:"))
            {
                operations.Add(ParseUpdateFile(lines, ref lineIndex));
                continue;
            }

            throw new FormatException($"Unrecognized patch line: '{currentLine}'.");
        }

        if (operations.Count == 0)
        {
            throw new FormatException("Patch must include at least one Add File, Delete File, or Update File section.");
        }

        return new PatchDocument(operations);
    }

    private static PatchOperation ParseAddFile(
        IReadOnlyList<string> lines,
        ref int lineIndex)
    {
        string path = ParseHeaderValue(lines[lineIndex], "*** Add File:");
        lineIndex++;

        List<string> fileLines = [];
        while (lineIndex < lines.Count &&
               !IsPatchOperationBoundary(lines[lineIndex]))
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
        string path = ParseHeaderValue(lines[lineIndex], "*** Delete File:");
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
        string path = ParseHeaderValue(lines[lineIndex], "*** Update File:");
        lineIndex++;

        string? moveToPath = null;
        if (lineIndex < lines.Count &&
            IsPatchHeader(lines[lineIndex], "*** Move to:"))
        {
            moveToPath = ParseHeaderValue(lines[lineIndex], "*** Move to:");
            lineIndex++;
        }

        List<PatchHunk> hunks = [];
        List<PatchLine>? currentHunkLines = null;
        string? currentChangeContext = null;
        bool currentHunkIsEndOfFile = false;

        while (lineIndex < lines.Count &&
               !IsPatchOperationBoundary(lines[lineIndex]))
        {
            string line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (CanSkipBlankUpdatePatchLine(lines, lineIndex, currentHunkLines))
                {
                    lineIndex++;
                    continue;
                }

                throw new FormatException(
                    "Blank lines inside update patches must either be prefixed with ' ', '+', or '-', or be placed between hunks or operations.");
            }

            if (string.Equals(line, "\\ No newline at end of file", StringComparison.Ordinal))
            {
                if (currentHunkLines is null || currentHunkLines.Count == 0)
                {
                    throw new FormatException("No-newline patch markers must follow a patch line.");
                }

                PatchLine previousLine = currentHunkLines[^1];
                currentHunkLines[^1] = previousLine with { NoNewlineAtEnd = true };
                lineIndex++;
                continue;
            }

            if (string.Equals(line, "*** End of File", StringComparison.Ordinal))
            {
                if (currentHunkLines is null)
                {
                    throw new FormatException("End-of-file patch markers must follow a hunk header.");
                }

                currentHunkIsEndOfFile = true;
                lineIndex++;
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (currentHunkLines is not null)
                {
                    hunks.Add(new PatchHunk(
                        currentHunkLines,
                        currentChangeContext,
                        currentHunkIsEndOfFile));
                }

                currentHunkLines = [];
                currentChangeContext = line[2..].Trim();
                if (currentChangeContext.Length == 0)
                {
                    currentChangeContext = null;
                }

                currentHunkIsEndOfFile = false;
                lineIndex++;
                continue;
            }

            if (currentHunkLines is not null &&
                currentHunkLines.Count == 0 &&
                !string.IsNullOrWhiteSpace(currentChangeContext) &&
                string.Equals(line, currentChangeContext, StringComparison.Ordinal))
            {
                currentHunkLines.Add(new PatchLine(
                    PatchLineKind.Context,
                    line,
                    NoNewlineAtEnd: false));
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
                line[1..],
                NoNewlineAtEnd: false));

            lineIndex++;
        }

        if (currentHunkLines is not null)
        {
            hunks.Add(new PatchHunk(
                currentHunkLines,
                currentChangeContext,
                currentHunkIsEndOfFile));
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

    private static bool CanSkipBlankUpdatePatchLine(
        IReadOnlyList<string> lines,
        int lineIndex,
        List<PatchLine>? currentHunkLines)
    {
        int nextNonBlankLineIndex = lineIndex + 1;
        while (nextNonBlankLineIndex < lines.Count &&
               string.IsNullOrWhiteSpace(lines[nextNonBlankLineIndex]))
        {
            nextNonBlankLineIndex++;
        }

        if (nextNonBlankLineIndex >= lines.Count)
        {
            return true;
        }

        string nextLine = lines[nextNonBlankLineIndex];
        bool isBoundary = nextLine.StartsWith("@@", StringComparison.Ordinal) ||
                          IsPatchOperationBoundary(nextLine);
        if (!isBoundary)
        {
            return false;
        }

        return currentHunkLines is null || currentHunkLines.Count > 0;
    }

    private static string StripPatchHeredoc(string patch)
    {
        string normalized = patch
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        string[] lines = normalized.Split('\n', StringSplitOptions.None);
        if (lines.Length < 3)
        {
            return patch;
        }

        if (!TryReadHeredocMarker(lines[0].Trim(), out string? marker))
        {
            return patch;
        }

        if (!string.Equals(lines[^1].Trim(), marker, StringComparison.Ordinal))
        {
            return patch;
        }

        return string.Join("\n", lines.Skip(1).Take(lines.Length - 2));
    }

    private static bool TryReadHeredocMarker(
        string line,
        out string? marker)
    {
        marker = null;
        if (line.StartsWith("cat ", StringComparison.Ordinal))
        {
            line = line["cat ".Length..].TrimStart();
        }

        if (!line.StartsWith("<<", StringComparison.Ordinal))
        {
            return false;
        }

        string value = line[2..].Trim();
        if (value.Length >= 2 &&
            ((value[0] == '\'' && value[^1] == '\'') ||
             (value[0] == '"' && value[^1] == '"')))
        {
            value = value[1..^1];
        }

        if (value.Length == 0 ||
            value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            return false;
        }

        marker = value;
        return true;
    }

    private static int FindPatchMarker(
        IReadOnlyList<string> lines,
        string marker)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (string.Equals(lines[index].Trim(), marker, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsPatchOperationBoundary(string line)
    {
        return string.Equals(line.Trim(), "*** End Patch", StringComparison.Ordinal) ||
               IsPatchHeader(line, "*** Add File:") ||
               IsPatchHeader(line, "*** Delete File:") ||
               IsPatchHeader(line, "*** Update File:");
    }

    private static bool IsPatchHeader(
        string line,
        string prefix)
    {
        return line.StartsWith(prefix, StringComparison.Ordinal);
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
        string[] originalLines = SplitLines(previousContent);
        List<PatchReplacement> replacements = [];
        int searchStart = 0;
        bool? trailingNewLineOverride = null;

        foreach (PatchHunk hunk in hunks)
        {
            int? changeContextIndex = null;
            if (!string.IsNullOrWhiteSpace(hunk.ChangeContext))
            {
                int contextIndex = SeekSequence(
                    originalLines,
                    [hunk.ChangeContext],
                    searchStart,
                    endOfFile: false);
                if (contextIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Could not apply the requested patch because context '{hunk.ChangeContext}' was not found in '{path}'.");
                }

                changeContextIndex = contextIndex;
                searchStart = contextIndex + 1;
            }

            string[] beforeLines = hunk.Lines
                .Where(static line => line.Kind is PatchLineKind.Context or PatchLineKind.Removal)
                .Select(static line => line.Text)
                .ToArray();
            string[] afterLines = hunk.Lines
                .Where(static line => line.Kind is PatchLineKind.Context or PatchLineKind.Addition)
                .Select(static line => line.Text)
                .ToArray();

            if (beforeLines.Length == 0)
            {
                int insertIndex = changeContextIndex is null
                    ? originalLines.Length
                    : changeContextIndex.Value + 1;
                replacements.Add(new PatchReplacement(insertIndex, 0, afterLines, hunk));
                continue;
            }

            string[] pattern = beforeLines;
            string[] replacementLines = afterLines;
            int patternSearchStart = changeContextIndex is not null &&
                                     beforeLines.Length > 0 &&
                                     string.Equals(beforeLines[0], hunk.ChangeContext, StringComparison.Ordinal)
                ? changeContextIndex.Value
                : searchStart;
            int matchIndex = SeekSequence(
                originalLines,
                pattern,
                patternSearchStart,
                hunk.IsEndOfFile);

            if (matchIndex < 0 &&
                pattern.Length > 0 &&
                pattern[^1].Length == 0)
            {
                pattern = pattern[..^1];
                if (replacementLines.Length > 0 && replacementLines[^1].Length == 0)
                {
                    replacementLines = replacementLines[..^1];
                }

                matchIndex = SeekSequence(
                    originalLines,
                    pattern,
                    patternSearchStart,
                    hunk.IsEndOfFile);
            }

            if (matchIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Could not apply the requested patch because the target context was not found in '{path}'.");
            }

            replacements.Add(new PatchReplacement(matchIndex, pattern.Length, replacementLines, hunk));
            searchStart = matchIndex + pattern.Length;
        }

        List<string> currentLines = originalLines.ToList();
        PatchReplacement[] orderedReplacements = replacements
            .OrderBy(static replacement => replacement.StartIndex)
            .ToArray();
        trailingNewLineOverride = GetTrailingNewLineOverride(path, originalLines.Length, orderedReplacements);
        for (int index = orderedReplacements.Length - 1; index >= 0; index--)
        {
            PatchReplacement replacement = orderedReplacements[index];
            currentLines.RemoveRange(replacement.StartIndex, replacement.OldLineCount);
            currentLines.InsertRange(replacement.StartIndex, replacement.NewLines);
        }

        bool trailingNewLine = trailingNewLineOverride ?? true;
        return JoinLines(currentLines, trailingNewLine);
    }

    private static bool? GetTrailingNewLineOverride(
        string path,
        int originalLineCount,
        IReadOnlyList<PatchReplacement> replacements)
    {
        bool? trailingNewLine = null;
        PatchReplacement? finalReplacement = replacements.Count == 0
            ? null
            : replacements[^1];

        foreach (PatchReplacement replacement in replacements)
        {
            int beforeIndex = -1;
            int afterIndex = -1;
            bool touchesEndOfFile = finalReplacement is not null &&
                                    replacement.Equals(finalReplacement.Value) &&
                                    replacement.StartIndex + replacement.OldLineCount == originalLineCount;

            foreach (PatchLine line in replacement.Hunk.Lines)
            {
                if (line.Kind is PatchLineKind.Context or PatchLineKind.Removal)
                {
                    beforeIndex++;
                }

                if (line.Kind is PatchLineKind.Context or PatchLineKind.Addition)
                {
                    afterIndex++;
                }

                if (!line.NoNewlineAtEnd)
                {
                    continue;
                }

                bool isValidMarker = line.Kind switch
                {
                    PatchLineKind.Context => touchesEndOfFile &&
                                             beforeIndex == replacement.OldLineCount - 1 &&
                                             afterIndex == replacement.NewLines.Count - 1,
                    PatchLineKind.Addition => touchesEndOfFile &&
                                              afterIndex == replacement.NewLines.Count - 1,
                    PatchLineKind.Removal => touchesEndOfFile &&
                                             beforeIndex == replacement.OldLineCount - 1,
                    _ => false
                };

                if (!isValidMarker)
                {
                    throw new FormatException(
                        $"No-newline patch markers must apply to the final resulting line in '{path}'.");
                }

                trailingNewLine = line.Kind is PatchLineKind.Addition or PatchLineKind.Context
                    ? false
                    : true;
            }
        }

        return trailingNewLine;
    }

    private static int SeekSequence(
        IReadOnlyList<string> source,
        IReadOnlyList<string> target,
        int startIndex,
        bool endOfFile)
    {
        if (target.Count == 0)
        {
            return -1;
        }

        Func<string, string, bool>[] comparers =
        [
            static (left, right) => string.Equals(left, right, StringComparison.Ordinal),
            static (left, right) => string.Equals(left.TrimEnd(), right.TrimEnd(), StringComparison.Ordinal),
            static (left, right) => string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal),
            static (left, right) => string.Equals(
                NormalizePatchMatchText(left.Trim()),
                NormalizePatchMatchText(right.Trim()),
                StringComparison.Ordinal)
        ];

        foreach (Func<string, string, bool> comparer in comparers)
        {
            int matchIndex = TryMatchSequence(source, target, startIndex, endOfFile, comparer);
            if (matchIndex >= 0)
            {
                return matchIndex;
            }
        }

        return -1;
    }

    private static int TryMatchSequence(
        IReadOnlyList<string> source,
        IReadOnlyList<string> target,
        int startIndex,
        bool endOfFile,
        Func<string, string, bool> comparer)
    {
        if (endOfFile)
        {
            int fromEndIndex = source.Count - target.Count;
            if (fromEndIndex >= Math.Max(0, startIndex) &&
                SequenceMatches(source, target, fromEndIndex, comparer))
            {
                return fromEndIndex;
            }
        }

        for (int index = Math.Max(0, startIndex); index <= source.Count - target.Count; index++)
        {
            if (SequenceMatches(source, target, index, comparer))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool SequenceMatches(
        IReadOnlyList<string> source,
        IReadOnlyList<string> target,
        int sourceIndex,
        Func<string, string, bool> comparer)
    {
        for (int offset = 0; offset < target.Count; offset++)
        {
            if (!comparer(source[sourceIndex + offset], target[offset]))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizePatchMatchText(string value)
    {
        return value
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'')
            .Replace('\u201A', '\'')
            .Replace('\u201B', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace('\u201E', '"')
            .Replace('\u201F', '"')
            .Replace('\u2010', '-')
            .Replace('\u2011', '-')
            .Replace('\u2012', '-')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u2015', '-')
            .Replace("\u2026", "...", StringComparison.Ordinal)
            .Replace('\u00A0', ' ');
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
        StringComparer pathComparer = WorkspacePath.GetPathComparer();
        return document.Operations
            .SelectMany(static operation => operation.MoveToPath is null
                ? [operation.Path]
                : new[] { operation.Path, operation.MoveToPath })
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(pathComparer)
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
        IReadOnlyList<PatchLine> Lines,
        string? ChangeContext,
        bool IsEndOfFile);

    private readonly record struct PatchLine(
        PatchLineKind Kind,
        string Text,
        bool NoNewlineAtEnd);

    private readonly record struct PatchReplacement(
        int StartIndex,
        int OldLineCount,
        IReadOnlyList<string> NewLines,
        PatchHunk Hunk);

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
