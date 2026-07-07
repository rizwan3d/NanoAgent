using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Workspaces;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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
    private const int LargeFileThresholdBytes = 262_144;
    private const int ContentPreviewThresholdChars = 262_144;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly HashSet<string> GeneratedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "node_modules",
        "vendor",
        "dist",
        "build",
        "out",
        "target",
        "coverage",
        ".next",
        ".nuxt",
        ".svelte-kit",
        "packages",
        "generated"
    };
    private static readonly string[] GeneratedFileSuffixes =
    [
        ".g.cs",
        ".g.i.cs",
        ".gen.cs",
        ".generated.cs",
        ".designer.cs",
        ".pb.cs",
        ".AssemblyInfo.cs"
    ];

    private readonly record struct FileEncodingInfo(bool HasBom, string NewLine);
    private readonly record struct WorkspaceFileSearchPage(
        IReadOnlyList<WorkspaceFileSearchMatch> Matches,
        int Offset,
        int Limit,
        int TotalMatchCount,
        bool HasMore,
        string? NextCursor);


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
        return await ApplyPatchWithTrackingAsync(
            document,
            await FilterLargeFilePathsAsync(
                GetTrackedPatchPaths(document),
                cancellationToken),
            cancellationToken);
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

            if (state.Content is null)
            {
                continue;
            }

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
        return await ExecuteTrackedSingleFileEditAsync(
            path,
            (service, trackedPath, token) => service.WriteFileAsync(trackedPath, content, overwrite, token),
            static result => $"file_write ({result.Path})",
            static (result, transaction) => new WorkspaceFileWriteExecutionResult(result, transaction),
            cancellationToken);
    }

    public async Task<WorkspaceFileDeleteExecutionResult> DeleteFileWithTrackingAsync(
        string path,
        CancellationToken cancellationToken)
    {
        return await ExecuteTrackedSingleFileEditAsync(
            path,
            static (service, trackedPath, token) => service.DeleteFileAsync(trackedPath, token),
            static result => $"file_delete ({result.Path})",
            static (result, transaction) => new WorkspaceFileDeleteExecutionResult(result, transaction),
            cancellationToken);
    }

    private async Task<WorkspaceApplyPatchExecutionResult> ApplyPatchWithTrackingAsync(
        PatchDocument document,
        string[] trackedPaths,
        CancellationToken cancellationToken)
    {
        WorkspaceFileEditState[] beforeStates = await CaptureFileStatesAsync(trackedPaths, cancellationToken);
        WorkspaceApplyPatchResult result = await ExecuteTrackedMutationAsync(
            beforeStates,
            (_, service, token) => service.ApplyPatchDocumentAsync(document, token),
            cancellationToken);

        WorkspaceFileEditTransaction? transaction = trackedPaths.Length == 0
            ? null
            : new WorkspaceFileEditTransaction(
                $"apply_patch ({result.FileCount} {(result.FileCount == 1 ? "file" : "files")})",
                beforeStates,
                await CaptureFileStatesAsync(trackedPaths, cancellationToken));

        return new WorkspaceApplyPatchExecutionResult(result, transaction);
    }

    private async Task<TExecutionResult> ExecuteTrackedSingleFileEditAsync<TResult, TExecutionResult>(
        string path,
        Func<WorkspaceFileService, string, CancellationToken, Task<TResult>> operation,
        Func<TResult, string> transactionDescriptionFactory,
        Func<TResult, WorkspaceFileEditTransaction, TExecutionResult> resultFactory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceFileEditState beforeState = await CaptureFileStateAsync(path, cancellationToken);
        TResult result = await ExecuteTrackedMutationAsync(
            [beforeState],
            (states, service, token) => operation(service, states[0].Path, token),
            cancellationToken);

        WorkspaceFileEditState afterState = await CaptureFileStateAsync(path, cancellationToken);
        return resultFactory(
            result,
            new WorkspaceFileEditTransaction(
                transactionDescriptionFactory(result),
                [beforeState],
                [afterState]));
    }

    private async Task<TResult> ExecuteTrackedMutationAsync<TResult>(
        WorkspaceFileEditState[] beforeStates,
        Func<WorkspaceFileEditState[], WorkspaceFileService, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation(beforeStates, this, cancellationToken);
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
                    "Workspace mutation failed and rollback did not complete successfully.",
                    exception,
                    rollbackException);
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
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
        string effectiveMode = GetEffectiveSearchMode(request);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            if (!request.IncludeIgnored)
            {
                EnsurePathNotIgnored(fullPath, Directory.Exists(fullPath), ignoreMatcher);
            }
        }

        WorkspaceFileSearchPage page = SearchFilesManaged(request, effectiveMode, fullPath, ignoreMatcher);

        return new WorkspaceFileSearchResult(
            request.Query,
            ToWorkspaceRelativePath(fullPath),
            page.Matches,
            request.Glob,
            request.Fuzzy,
            page.Limit,
            request.CaseSensitive,
            effectiveMode,
            request.Regex,
            request.WholeWord,
            page.Offset,
            request.Cursor,
            page.NextCursor,
            page.HasMore,
            page.TotalMatchCount,
            request.IncludeHidden,
            request.IncludeGenerated,
            request.IncludeIgnored,
            request.IncludeGlobs ?? [],
            request.ExcludeGlobs ?? []);
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

        FileEncodingInfo encoding = fileExists
            ? DetectFileEncoding(fullPath)
            : new FileEncodingInfo(HasBom: false, NewLine: "\n");
        EnsureParentDirectory(fullPath);

        await WriteAllTextWithEncodingAsync(
            fullPath,
            content,
            encoding,
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

        if (TryGetFileLength(fullPath, out long fileLength) && fileLength > LargeFileThresholdBytes)
        {
            await using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            byte[] hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
            string hash = Convert.ToHexStringLower(hashBytes);
            return new WorkspaceFileEditState(
                ToWorkspaceRelativePath(fullPath),
                exists: true,
                content: null,
                contentHash: hash);
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
        FileEncodingInfo encoding = new FileEncodingInfo(HasBom: false, NewLine: "\n");


        EnsureParentDirectory(fullPath);
        await WriteAllTextWithEncodingAsync(
            fullPath,
            content,
            encoding,
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
        FileEncodingInfo encoding = DetectFileEncoding(currentFullPath);
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

        await WriteAllTextWithEncodingAsync(
            destinationFullPath,
            updatedContent,
            encoding,
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

    private WorkspaceFileSearchPage SearchFilesManaged(
        WorkspaceFileSearchRequest request,
        string effectiveMode,
        string fullPath,
        WorkspaceIgnoreMatcher ignoreMatcher)
    {
        bool searchPathIsDirectory = Directory.Exists(fullPath);
        bool searchPathExists = File.Exists(fullPath) || searchPathIsDirectory;
        if (searchPathExists &&
            !string.Equals(ToWorkspaceRelativePath(fullPath), ".", StringComparison.Ordinal) &&
            !ShouldIncludeSearchPath(
                ToWorkspaceRelativePath(fullPath),
                fullPath,
                searchPathIsDirectory,
                request,
                ignoreMatcher))
        {
            int initialOffset = GetSearchOffset(request);
            return new WorkspaceFileSearchPage([], initialOffset, Math.Clamp(request.Limit, 1, MaxFileSearchResults), 0, false, null);
        }

        IEnumerable<string> files = File.Exists(fullPath)
            ? [fullPath]
            : Directory.Exists(fullPath)
                ? EnumerateSearchFiles(fullPath, request, ignoreMatcher)
                : throw new FileNotFoundException(
                    $"Search path '{request.Path ?? "."}' does not exist.");

        List<WorkspaceFileSearchMatch> matches = files
            .Select(filePath => CreateSearchMatch(request, effectiveMode, filePath, ignoreMatcher))
            .Where(static match => match is not null)
            .Select(static match => match!)
            .OrderByDescending(static match => match.Score)
            .ThenBy(static match => match.Path, StringComparer.Ordinal)
            .ToList();

        int limit = Math.Clamp(request.Limit, 1, MaxFileSearchResults);
        int offset = GetSearchOffset(request);
        if (offset > matches.Count)
        {
            offset = matches.Count;
        }

        WorkspaceFileSearchMatch[] pageMatches = matches
            .Skip(offset)
            .Take(limit)
            .ToArray();
        bool hasMore = offset + pageMatches.Length < matches.Count;

        return new WorkspaceFileSearchPage(
            pageMatches,
            offset,
            limit,
            matches.Count,
            hasMore,
            hasMore
                ? EncodeSearchCursor(offset + pageMatches.Length)
                : null);
    }

    private WorkspaceFileSearchMatch? CreateSearchMatch(
        WorkspaceFileSearchRequest request,
        string effectiveMode,
        string filePath,
        WorkspaceIgnoreMatcher ignoreMatcher)
    {
        string relativePath = ToWorkspaceRelativePath(filePath);
        if (!ShouldIncludeSearchPath(relativePath, filePath, isDirectory: false, request, ignoreMatcher))
        {
            return null;
        }

        return TryMatchSearchPath(request, effectiveMode, relativePath);
    }

    private static WorkspaceFileSearchMatch? TryMatchSearchPath(
        WorkspaceFileSearchRequest request,
        string mode,
        string relativePath)
    {
        return mode switch
        {
            WorkspaceFileSearchModes.Fuzzy => TryGetFuzzySearchMatch(relativePath, request.Query, request.CaseSensitive),
            WorkspaceFileSearchModes.Exact => TryGetExactSearchMatch(relativePath, request.Query, request.CaseSensitive),
            WorkspaceFileSearchModes.Regex => TryGetRegexSearchMatch(relativePath, request.Query, request.CaseSensitive, request.WholeWord),
            WorkspaceFileSearchModes.GlobOnly => CreateGlobOnlySearchMatch(relativePath),
            _ => TryGetSubstringSearchMatch(relativePath, request.Query, request.CaseSensitive, request.WholeWord)
        };
    }

    private bool ShouldIncludeSearchPath(
        string relativePath,
        string fullPath,
        bool isDirectory,
        WorkspaceFileSearchRequest request,
        WorkspaceIgnoreMatcher ignoreMatcher)
    {
        if (!request.IncludeIgnored &&
            ignoreMatcher.IsIgnored(fullPath, isDirectory))
        {
            return false;
        }

        if (!request.IncludeHidden &&
            IsHiddenPath(relativePath, fullPath, isDirectory))
        {
            return false;
        }

        if (!request.IncludeGenerated &&
            IsGeneratedOrVendorPath(relativePath))
        {
            return false;
        }

        IReadOnlyList<string> includeGlobs = GetEffectiveIncludeGlobs(request);
        if (!isDirectory &&
            includeGlobs.Count > 0 &&
            !includeGlobs.Any(glob => WorkspaceIgnoreMatcher.MatchesGlob(glob, relativePath, isDirectory)))
        {
            return false;
        }

        if (request.ExcludeGlobs?.Count > 0 &&
            request.ExcludeGlobs.Any(glob => WorkspaceIgnoreMatcher.MatchesGlob(glob, relativePath, isDirectory)))
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> GetEffectiveIncludeGlobs(WorkspaceFileSearchRequest request)
    {
        if (request.IncludeGlobs?.Count > 0)
        {
            return request.IncludeGlobs;
        }

        return string.IsNullOrWhiteSpace(request.Glob)
            ? []
            : [request.Glob];
    }

    private static string GetEffectiveSearchMode(WorkspaceFileSearchRequest request)
    {
        if (request.Regex &&
            (string.IsNullOrWhiteSpace(request.Mode) ||
             string.Equals(request.Mode, WorkspaceFileSearchModes.Substring, StringComparison.Ordinal)))
        {
            return WorkspaceFileSearchModes.Regex;
        }

        if (request.Fuzzy &&
            (string.IsNullOrWhiteSpace(request.Mode) ||
             string.Equals(request.Mode, WorkspaceFileSearchModes.Substring, StringComparison.Ordinal)))
        {
            return WorkspaceFileSearchModes.Fuzzy;
        }

        return string.IsNullOrWhiteSpace(request.Mode)
            ? WorkspaceFileSearchModes.Substring
            : request.Mode;
    }

    private IEnumerable<string> EnumerateSearchFiles(
        string root,
        WorkspaceFileSearchRequest request,
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
                string relativePath = ToWorkspaceRelativePath(entry);
                if (!ShouldIncludeSearchPath(relativePath, entry, isDirectory, request, ignoreMatcher))
                {
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

                yield return entry;
            }
        }
    }

    private static WorkspaceFileSearchMatch? TryGetSubstringSearchMatch(
        string relativePath,
        string query,
        bool caseSensitive,
        bool wholeWord)
    {
        string fileName = Path.GetFileName(relativePath);
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (wholeWord)
        {
            if (ContainsWholeWord(fileName, query, caseSensitive))
            {
                return new WorkspaceFileSearchMatch(relativePath, 9_200 - relativePath.Length, "filename_whole_word");
            }

            if (ContainsWholeWord(relativePath, query, caseSensitive))
            {
                return new WorkspaceFileSearchMatch(relativePath, 8_700 - relativePath.Length, "path_whole_word");
            }

            return null;
        }

        if (string.Equals(fileName, query, comparison))
        {
            return new WorkspaceFileSearchMatch(relativePath, 10_000 - relativePath.Length, "filename_exact");
        }

        if (string.Equals(relativePath, query, comparison))
        {
            return new WorkspaceFileSearchMatch(relativePath, 9_500 - relativePath.Length, "path_exact");
        }

        if (fileName.Contains(query, comparison))
        {
            return new WorkspaceFileSearchMatch(relativePath, 9_000 - relativePath.Length, "filename_contains");
        }

        if (relativePath.Contains(query, comparison))
        {
            return new WorkspaceFileSearchMatch(relativePath, 8_500 - relativePath.Length, "path_contains");
        }

        return null;
    }

    private static WorkspaceFileSearchMatch? TryGetExactSearchMatch(
        string relativePath,
        string query,
        bool caseSensitive)
    {
        string fileName = Path.GetFileName(relativePath);
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (string.Equals(fileName, query, comparison))
        {
            return new WorkspaceFileSearchMatch(relativePath, 10_000 - relativePath.Length, "filename_exact");
        }

        if (string.Equals(relativePath, query, comparison))
        {
            return new WorkspaceFileSearchMatch(relativePath, 9_500 - relativePath.Length, "path_exact");
        }

        return null;
    }

    private static WorkspaceFileSearchMatch? TryGetRegexSearchMatch(
        string relativePath,
        string query,
        bool caseSensitive,
        bool wholeWord)
    {
        string pattern = wholeWord
            ? WrapWholeWordPattern(query)
            : query;
        Regex regex = new(pattern, GetSearchRegexOptions(caseSensitive));
        string fileName = Path.GetFileName(relativePath);

        if (regex.IsMatch(fileName))
        {
            return new WorkspaceFileSearchMatch(relativePath, 8_900 - relativePath.Length, "filename_regex");
        }

        if (regex.IsMatch(relativePath))
        {
            return new WorkspaceFileSearchMatch(relativePath, 8_400 - relativePath.Length, "path_regex");
        }

        return null;
    }

    private static WorkspaceFileSearchMatch CreateGlobOnlySearchMatch(string relativePath)
    {
        return new WorkspaceFileSearchMatch(relativePath, 1_000 - relativePath.Length, "glob");
    }

    private static WorkspaceFileSearchMatch? TryGetFuzzySearchMatch(
        string relativePath,
        string query,
        bool caseSensitive)
    {
        string fileName = Path.GetFileName(relativePath);
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (string.Equals(fileName, query, comparison))
        {
            return new WorkspaceFileSearchMatch(relativePath, 10_000 - relativePath.Length, "filename_exact");
        }

        if (string.Equals(relativePath, query, comparison))
        {
            return new WorkspaceFileSearchMatch(relativePath, 9_500 - relativePath.Length, "path_exact");
        }

        if (fileName.Contains(query, comparison))
        {
            return new WorkspaceFileSearchMatch(relativePath, 9_000 - relativePath.Length, "filename_contains");
        }

        if (relativePath.Contains(query, comparison))
        {
            return new WorkspaceFileSearchMatch(relativePath, 8_500 - relativePath.Length, "path_contains");
        }

        if (TryScoreSubsequence(fileName, query, caseSensitive, out int fileNameScore))
        {
            return new WorkspaceFileSearchMatch(relativePath, 7_000 + fileNameScore, "filename_subsequence");
        }

        if (TryScoreSubsequence(relativePath, query, caseSensitive, out int relativePathScore))
        {
            return new WorkspaceFileSearchMatch(relativePath, 6_000 + relativePathScore, "path_subsequence");
        }

        return null;
    }

    private static bool ContainsWholeWord(
        string candidate,
        string query,
        bool caseSensitive)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        Regex regex = new(
            WrapWholeWordPattern(Regex.Escape(query)),
            GetSearchRegexOptions(caseSensitive));
        return regex.IsMatch(candidate);
    }

    private static string WrapWholeWordPattern(string pattern)
    {
        return $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
    }

    private static RegexOptions GetSearchRegexOptions(bool caseSensitive)
    {
        return caseSensitive
            ? RegexOptions.CultureInvariant
            : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    }

    private static int GetSearchOffset(WorkspaceFileSearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Cursor) &&
            TryDecodeSearchCursor(request.Cursor, out int cursorOffset))
        {
            return cursorOffset;
        }

        return Math.Max(0, request.Offset);
    }

    private static string EncodeSearchCursor(int offset)
    {
        return Convert.ToBase64String(
            Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));
    }

    private static bool TryDecodeSearchCursor(
        string cursor,
        out int offset)
    {
        offset = 0;

        try
        {
            byte[] bytes = Convert.FromBase64String(cursor);
            string text = Encoding.UTF8.GetString(bytes);
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out offset) && offset >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsHiddenPath(
        string relativePath,
        string fullPath,
        bool isDirectory)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(fullPath);
            if (attributes.HasFlag(FileAttributes.Hidden))
            {
                return true;
            }
        }
        catch (Exception exception) when (IsFileSystemAccessException(exception))
        {
        }

        string normalized = relativePath.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int segmentCount = isDirectory ? segments.Length : Math.Max(0, segments.Length - 1);
        for (int index = 0; index < segments.Length; index++)
        {
            if (index < segmentCount &&
                segments[index].Length > 0 &&
                segments[index][0] == '.' &&
                !string.Equals(segments[index], ".", StringComparison.Ordinal) &&
                !string.Equals(segments[index], "..", StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (!isDirectory &&
            segments.Length > 0 &&
            segments[^1].Length > 0 &&
            segments[^1][0] == '.' &&
            !string.Equals(segments[^1], ".", StringComparison.Ordinal) &&
            !string.Equals(segments[^1], "..", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool IsGeneratedOrVendorPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (GeneratedDirectoryNames.Contains(segment))
            {
                return true;
            }
        }

        string fileName = Path.GetFileName(relativePath);
        return GeneratedFileSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) ||
            fileName.Contains(".generated.", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(".designer.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryScoreSubsequence(
        string candidate,
        string query,
        bool caseSensitive,
        out int score)
    {
        score = 0;
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(query))
        {
            return false;
        }

        int firstMatchIndex = -1;
        int previousMatchIndex = -1;
        int gapPenalty = 0;
        int consecutiveBonus = 0;

        for (int queryIndex = 0, candidateIndex = 0; queryIndex < query.Length; queryIndex++)
        {
            bool found = false;
            while (candidateIndex < candidate.Length)
            {
                if (CharsEqual(candidate[candidateIndex], query[queryIndex], caseSensitive))
                {
                    if (firstMatchIndex < 0)
                    {
                        firstMatchIndex = candidateIndex;
                    }

                    if (previousMatchIndex >= 0)
                    {
                        if (candidateIndex == previousMatchIndex + 1)
                        {
                            consecutiveBonus += 4;
                        }
                        else
                        {
                            gapPenalty += candidateIndex - previousMatchIndex - 1;
                        }
                    }

                    previousMatchIndex = candidateIndex;
                    candidateIndex++;
                    found = true;
                    break;
                }

                candidateIndex++;
            }

            if (!found)
            {
                score = 0;
                return false;
            }
        }

        score = 500
            - (firstMatchIndex * 2)
            - gapPenalty
            - Math.Max(0, candidate.Length - query.Length)
            + consecutiveBonus;
        return true;
    }

    private static bool CharsEqual(
        char left,
        char right,
        bool caseSensitive)
    {
        return caseSensitive
            ? left == right
            : char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
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
        if (!ignoreMatcher.TryGetIgnoreSource(fullPath, isDirectory, out string sourceDisplayPath))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Path '{ToWorkspaceRelativePath(fullPath)}' is excluded by {sourceDisplayPath}.");
    }

    private string ToWorkspaceRelativePath(string fullPath)
    {
        return WorkspacePath.ToRelativePath(GetWorkspaceRoot(), fullPath);
    }

    private string GetWorkspaceRoot()
    {
        return Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
    }

    private static FileEncodingInfo DetectFileEncoding(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return new FileEncodingInfo(HasBom: false, NewLine: "\n");
        }

        byte[] bytes = File.ReadAllBytes(fullPath);

        bool hasBom = bytes.Length >= 3 &&
                      bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        int start = hasBom ? 3 : 0;
        bool hasCrlf = false;
        for (int i = start; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n')
            {
                hasCrlf = true;
                break;
            }
        }

        return new FileEncodingInfo(hasBom, hasCrlf ? "\r\n" : "\n");
    }

    private static async Task WriteAllTextWithEncodingAsync(
        string fullPath,
        string content,
        FileEncodingInfo encoding,
        CancellationToken cancellationToken)
    {
        string finalContent = NormalizeNewlines(content, encoding.NewLine);
        Encoding writeEncoding = encoding.HasBom ? Utf8WithBom : Utf8NoBom;
        await File.WriteAllTextAsync(fullPath, finalContent, writeEncoding, cancellationToken);
    }

    private static string NormalizeNewlines(string content, string targetNewLine)
    {
        string normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return targetNewLine == "\r\n"
            ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal)
            : normalized;
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

        string[] lines = NormalizePatchInput(patch)
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

    private static string NormalizePatchInput(string patch)
    {
        string normalized = StripPatchHeredoc(StripMarkdownCodeFence(patch).Trim())
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        if (!normalized.Contains("*** Begin Patch", StringComparison.Ordinal) &&
            LooksLikeUnifiedDiff(normalized))
        {
            normalized = ConvertUnifiedDiffToApplyPatch(normalized);
        }

        return AutoRepairApplyPatchText(normalized);
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
            if (line.Length == 0)
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
                ValidateApplyPatchHunkHeader(line);
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

        if (moveToPath is null &&
            !hunks.Any(static hunk => hunk.Lines.Any(static line =>
                line.Kind is PatchLineKind.Addition or PatchLineKind.Removal)))
        {
            throw new FormatException(
                "Update file operations must include at least one added or removed line. " +
                "Prefix changed lines with '+' or '-'; do not submit context-only hunks.");
        }

        return new PatchOperation(
            PatchOperationKind.Update,
            path,
            moveToPath,
            [],
            hunks);
    }

    private static void ValidateApplyPatchHunkHeader(string line)
    {
        if (line == "@@")
        {
            return;
        }
    
        if (!line.StartsWith("@@ ", StringComparison.Ordinal))
        {
            throw new FormatException(
                "Invalid hunk header. Use '@@' or '@@ <anchor text>'.");
        }
    
        string anchor = line[3..];
    
        if (anchor.Length == 0)
        {
            throw new FormatException(
                "Invalid hunk header. Use '@@' for an empty locator.");
        }
    
        if (anchor[0] is '+' or '-' or ' ')
        {
            throw new FormatException(
                "Invalid hunk header. Do not put added, removed, or context lines on the '@@' locator line. " +
                "Put them inside the hunk with '+', '-', or ' ' prefixes.");
        }
    
        if (anchor.Contains("@@", StringComparison.Ordinal))
        {
            throw new FormatException(
                "Invalid hunk header. Unified diff ranges like '@@ -1,2 +1,2 @@' are not valid inside apply_patch format. " +
                "Use '@@' or '@@ <anchor text>'.");
        }
    }

    private static bool CanSkipBlankUpdatePatchLine(
        IReadOnlyList<string> lines,
        int lineIndex,
        List<PatchLine>? currentHunkLines)
    {
        int nextNonBlankLineIndex = lineIndex + 1;
        while (nextNonBlankLineIndex < lines.Count &&
               lines[nextNonBlankLineIndex].Length == 0)
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

    private static string StripMarkdownCodeFence(string patch)
    {
        string normalized = patch
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        string[] lines = normalized.Split('\n', StringSplitOptions.None);
        if (lines.Length < 3)
        {
            return patch;
        }

        int startIndex = 0;
        while (startIndex < lines.Length && string.IsNullOrWhiteSpace(lines[startIndex]))
        {
            startIndex++;
        }

        int endIndex = lines.Length - 1;
        while (endIndex >= 0 && string.IsNullOrWhiteSpace(lines[endIndex]))
        {
            endIndex--;
        }

        if (startIndex >= endIndex)
        {
            return patch;
        }

        if (!lines[startIndex].TrimStart().StartsWith("```", StringComparison.Ordinal) ||
            !string.Equals(lines[endIndex].Trim(), "```", StringComparison.Ordinal))
        {
            return patch;
        }

        return string.Join("\n", lines[(startIndex + 1)..endIndex]);
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

    private static bool LooksLikeUnifiedDiff(string patch)
    {
        string[] lines = patch.Split('\n', StringSplitOptions.None);
        bool sawOldPath = false;
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            if (!sawOldPath)
            {
                sawOldPath = line.StartsWith("--- ", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                return true;
            }

            sawOldPath = false;
        }

        return false;
    }

    private static string ConvertUnifiedDiffToApplyPatch(string patch)
    {
        string[] lines = patch.Split('\n', StringSplitOptions.None);
        List<string> converted = ["*** Begin Patch"];
        int index = 0;

        while (index < lines.Length)
        {
            string line = lines[index].TrimEnd();
            if (!line.StartsWith("--- ", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            if (index + 1 >= lines.Length ||
                !lines[index + 1].TrimEnd().StartsWith("+++ ", StringComparison.Ordinal))
            {
                throw new FormatException("Unified diff sections must include matching '---' and '+++' headers.");
            }

            string oldPath = ParseUnifiedDiffPath(lines[index].TrimEnd()["--- ".Length..]);
            string newPath = ParseUnifiedDiffPath(lines[index + 1].TrimEnd()["+++ ".Length..]);
            index += 2;

            if (string.Equals(oldPath, "/dev/null", StringComparison.Ordinal))
            {
                converted.Add($"*** Add File: {newPath}");
                while (index < lines.Length && !lines[index].TrimEnd().StartsWith("--- ", StringComparison.Ordinal))
                {
                    string hunkLine = lines[index].TrimEnd();
                    if (hunkLine.StartsWith("@@", StringComparison.Ordinal) ||
                        hunkLine.Length == 0)
                    {
                        index++;
                        continue;
                    }

                    if (hunkLine == "\\ No newline at end of file")
                    {
                        index++;
                        continue;
                    }

                    if (!hunkLine.StartsWith('+') || hunkLine.StartsWith("+++ ", StringComparison.Ordinal))
                    {
                        throw new FormatException("Unified diff add-file hunks must contain only added lines.");
                    }

                    converted.Add(hunkLine);
                    index++;
                }

                continue;
            }

            if (string.Equals(newPath, "/dev/null", StringComparison.Ordinal))
            {
                converted.Add($"*** Delete File: {oldPath}");
                while (index < lines.Length && !lines[index].TrimEnd().StartsWith("--- ", StringComparison.Ordinal))
                {
                    index++;
                }

                continue;
            }

            converted.Add($"*** Update File: {oldPath}");
            if (!string.Equals(oldPath, newPath, StringComparison.Ordinal))
            {
                converted.Add($"*** Move to: {newPath}");
            }

            while (index < lines.Length && !lines[index].TrimEnd().StartsWith("--- ", StringComparison.Ordinal))
            {
                string hunkLine = lines[index].TrimEnd();
                if (hunkLine.Length == 0)
                {
                    index++;
                    continue;
                }

                if (hunkLine.StartsWith("@@", StringComparison.Ordinal))
                {
                    converted.Add(ConvertUnifiedDiffHunkHeader(hunkLine));
                    index++;
                    continue;
                }

                if (hunkLine == "\\ No newline at end of file")
                {
                    converted.Add(hunkLine);
                    index++;
                    continue;
                }

                if (hunkLine[0] is ' ' or '+' or '-')
                {
                    converted.Add(hunkLine);
                    index++;
                    continue;
                }

                index++;
            }
        }

        converted.Add("*** End Patch");
        return string.Join("\n", converted);
    }

    private static string ParseUnifiedDiffPath(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new FormatException("Unified diff file headers must include a path.");
        }

        if (string.Equals(trimmed, "/dev/null", StringComparison.Ordinal))
        {
            return trimmed;
        }

        int tabIndex = trimmed.IndexOf('\t');
        if (tabIndex >= 0)
        {
            trimmed = trimmed[..tabIndex];
        }

        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1];
        }

        if (trimmed.StartsWith("a/", StringComparison.Ordinal) ||
            trimmed.StartsWith("b/", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        return trimmed;
    }

    private static string ConvertUnifiedDiffHunkHeader(string line)
    {
        int closingIndex = line.IndexOf("@@", 2, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return "@@";
        }

        string trailing = line[(closingIndex + 2)..].Trim();
        return trailing.Length == 0
            ? "@@"
            : $"@@ {trailing}";
    }

    private static IEnumerable<string> RepairApplyPatchHunkHeader(string line)
    {
        if (IsUnifiedDiffHunkHeader(line))
        {
            yield return ConvertUnifiedDiffHunkHeader(line);
            yield break;
        }

        if (line == "@@" || !line.StartsWith("@@ ", StringComparison.Ordinal))
        {
            yield return line;
            yield break;
        }

        string locator = line[3..];
        if (locator.Length == 0)
        {
            yield return "@@";
            yield break;
        }

        if (locator[0] is '+' or '-' or ' ')
        {
            yield return "@@";
            yield return locator;
            yield break;
        }

        yield return line;
    }

    private static bool IsUnifiedDiffHunkHeader(string line)
    {
        if (!line.StartsWith("@@ -", StringComparison.Ordinal))
        {
            return false;
        }

        int closingIndex = line.IndexOf("@@", 2, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return false;
        }

        string ranges = line[3..closingIndex].Trim();
        return Regex.IsMatch(
            ranges,
            @"^-\d+(?:,\d+)? \+\d+(?:,\d+)?$",
            RegexOptions.CultureInvariant);
    }

    private static string AutoRepairApplyPatchText(string patch)
    {
        string[] lines = patch.Split('\n', StringSplitOptions.None);
        List<string> repaired = new(lines.Length + 4);
        PatchOperationKind? currentOperation = null;
        bool insideUpdateHunk = false;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];

            if (IsPatchHeader(line, "*** Add File:"))
            {
                currentOperation = PatchOperationKind.Add;
                insideUpdateHunk = false;
                repaired.Add(line);
                continue;
            }

            if (IsPatchHeader(line, "*** Delete File:"))
            {
                currentOperation = PatchOperationKind.Delete;
                insideUpdateHunk = false;
                repaired.Add(line);
                continue;
            }

            if (IsPatchHeader(line, "*** Update File:"))
            {
                currentOperation = PatchOperationKind.Update;
                insideUpdateHunk = false;
                repaired.Add(line);
                continue;
            }

            if (IsPatchHeader(line, "*** Move to:") ||
                string.Equals(line.Trim(), "*** Begin Patch", StringComparison.Ordinal) ||
                string.Equals(line.Trim(), "*** End Patch", StringComparison.Ordinal))
            {
                repaired.Add(line);
                continue;
            }

            if (currentOperation == PatchOperationKind.Add && line.Length == 0)
            {
                if (!CanTreatBlankUpdateLineAsSeparator(lines, index))
                {
                    repaired.Add("+");
                }
                continue;
            }

            if (currentOperation == PatchOperationKind.Update)
            {
                if (line.StartsWith("@@@", StringComparison.Ordinal))
                {
                    line = "@@" + line[3..];
                }

                if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    insideUpdateHunk = true;
                    foreach (string repairedLine in RepairApplyPatchHunkHeader(line))
                    {
                        repaired.Add(repairedLine);
                    }
                    continue;
                }

                if (!insideUpdateHunk &&
                    IsLikelyUpdatePatchContent(line))
                {
                    insideUpdateHunk = true;
                    repaired.Add("@@");
                }

                if (insideUpdateHunk &&
                    line.Length == 0 &&
                    !CanTreatBlankUpdateLineAsSeparator(lines, index))
                {
                    repaired.Add(InferBlankUpdateLinePrefix(lines, index));
                    continue;
                }
            }

            repaired.Add(line);
        }

        return string.Join("\n", repaired);
    }

    private static bool IsLikelyUpdatePatchContent(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        if (line[0] is ' ' or '+' or '-' ||
            string.Equals(line, "\\ No newline at end of file", StringComparison.Ordinal) ||
            string.Equals(line, "*** End of File", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string InferBlankUpdateLinePrefix(
        IReadOnlyList<string> lines,
        int lineIndex)
    {
        char? previousPrefix = FindNearbyPatchPrefix(lines, lineIndex, searchBackward: true);
        char? nextPrefix = FindNearbyPatchPrefix(lines, lineIndex, searchBackward: false);

        if (previousPrefix.HasValue && nextPrefix.HasValue && previousPrefix.Value == nextPrefix.Value)
        {
            return previousPrefix.Value.ToString();
        }

        if (previousPrefix.HasValue)
        {
            return previousPrefix.Value.ToString();
        }

        if (nextPrefix.HasValue)
        {
            return nextPrefix.Value.ToString();
        }

        return " ";
    }

    private static bool CanTreatBlankUpdateLineAsSeparator(
        IReadOnlyList<string> lines,
        int lineIndex)
    {
        int nextIndex = lineIndex + 1;
        while (nextIndex < lines.Count && lines[nextIndex].Length == 0)
        {
            nextIndex++;
        }

        if (nextIndex >= lines.Count)
        {
            return true;
        }

        string nextLine = lines[nextIndex];
        return nextLine.StartsWith("@@", StringComparison.Ordinal) ||
               IsPatchOperationBoundary(nextLine);
    }

    private static char? FindNearbyPatchPrefix(
        IReadOnlyList<string> lines,
        int lineIndex,
        bool searchBackward)
    {
        int index = lineIndex + (searchBackward ? -1 : 1);
        while (index >= 0 && index < lines.Count)
        {
            string candidate = lines[index];
            if (candidate.Length == 0)
            {
                index += searchBackward ? -1 : 1;
                continue;
            }

            if (candidate.StartsWith("@@", StringComparison.Ordinal) ||
                IsPatchOperationBoundary(candidate) ||
                IsPatchHeader(candidate, "*** Move to:"))
            {
                return null;
            }

            return candidate[0] is ' ' or '+' or '-'
                ? candidate[0]
                : null;
        }

        return null;
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
                int contextIndex = FindFirstSequenceMatch(
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
            PatchSequenceSearchResult match = FindUniqueSequenceMatch(
                originalLines,
                pattern,
                patternSearchStart,
                hunk.IsEndOfFile);

            if (match.Status == PatchSequenceSearchStatus.NotFound &&
                pattern.Length > 0 &&
                pattern[^1].Length == 0)
            {
                pattern = pattern[..^1];
                if (replacementLines.Length > 0 && replacementLines[^1].Length == 0)
                {
                    replacementLines = replacementLines[..^1];
                }

                match = FindUniqueSequenceMatch(
                    originalLines,
                    pattern,
                    patternSearchStart,
                    hunk.IsEndOfFile);
            }

            if (match.Status == PatchSequenceSearchStatus.NotFound)
            {
                PatchRetryMatch? retryMatch = TryFindRetryMatch(
                    originalLines,
                    hunk,
                    patternSearchStart,
                    hunk.IsEndOfFile);
                if (retryMatch is not null)
                {
                    replacements.Add(new PatchReplacement(
                        retryMatch.Value.StartIndex,
                        retryMatch.Value.OldLineCount,
                        retryMatch.Value.NewLines,
                        hunk));
                    searchStart = retryMatch.Value.StartIndex + retryMatch.Value.OldLineCount;
                    continue;
                }
            }

            // New fallback:
            // If the full hunk and retry windows fail, try applying each independent
            // removal/addition pair as a unique exact replacement.
            //
            // This fixes cases like XAML:
            // -<Run Text="{Binding InvoiceDisplay}"/>
            // +<Run Text="{Binding InvoiceDisplay, Mode=OneWay}"/>
            //
            // where stale surrounding context makes the full hunk fail, but the exact
            // old text still exists uniquely in the current file.
            if (match.Status == PatchSequenceSearchStatus.NotFound)
            {
                IReadOnlyList<PatchReplacement>? independentReplacements =
                    TryFindIndependentReplacementMatches(originalLines, hunk);

                if (independentReplacements is not null)
                {
                    replacements.AddRange(independentReplacements);

                    searchStart = independentReplacements
                        .Max(static replacement =>
                            replacement.StartIndex + replacement.OldLineCount);

                    continue;
                }
            }

            if (match.Status == PatchSequenceSearchStatus.NotFound)
            {
                throw new InvalidOperationException(
                    $"Could not apply the requested patch because the target context was not found in '{path}'. " +
                    $"Tried the full hunk and smaller retry windows near line {patternSearchStart + 1}.");
            }

            if (match.Status == PatchSequenceSearchStatus.Ambiguous)
            {
                throw new InvalidOperationException(
                    $"Could not apply the requested patch because it matched multiple locations in '{path}' " +
                    $"using {match.MatchStyle}. Candidate starting lines: {DescribeCandidateLines(match.CandidateStartIndexes)}.");
            }

            replacements.Add(new PatchReplacement(match.StartIndex, pattern.Length, replacementLines, hunk));
            searchStart = match.StartIndex + pattern.Length;
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

    private static IReadOnlyList<PatchReplacement>? TryFindIndependentReplacementMatches(
    IReadOnlyList<string> originalLines,
    PatchHunk hunk)
    {
        IReadOnlyList<PatchIndependentReplacementGroup> groups =
            BuildIndependentReplacementGroups(hunk);

        if (groups.Count == 0)
        {
            return null;
        }

        List<PatchReplacement> replacements = [];

        foreach (PatchIndependentReplacementGroup group in groups)
        {
            PatchReplacement? substringReplacement =
                TryFindIndependentSubstringReplacement(originalLines, group, hunk);

            if (substringReplacement is not null)
            {
                replacements.Add(substringReplacement.Value);
                continue;
            }

            PatchSequenceSearchResult match = FindUniqueSequenceMatch(
                originalLines,
                group.BeforeLines,
                startIndex: 0,
                endOfFile: false);

            if (match.Status == PatchSequenceSearchStatus.NotFound)
            {
                return null;
            }

            if (match.Status == PatchSequenceSearchStatus.Ambiguous)
            {
                throw new InvalidOperationException(
                    "Could not apply the requested patch because an independent replacement matched multiple locations. " +
                    $"Candidate starting lines: {DescribeCandidateLines(match.CandidateStartIndexes)}.");
            }

            replacements.Add(new PatchReplacement(
                match.StartIndex,
                group.BeforeLines.Count,
                group.AfterLines,
                hunk));
        }

        PatchReplacement[] ordered = replacements
            .OrderBy(static replacement => replacement.StartIndex)
            .ToArray();

        for (int index = 1; index < ordered.Length; index++)
        {
            int previousEnd = ordered[index - 1].StartIndex + ordered[index - 1].OldLineCount;
            if (ordered[index].StartIndex < previousEnd)
            {
                throw new InvalidOperationException(
                    "Could not apply the requested patch because independent replacements overlap.");
            }
        }

        return ordered;
    }

    private static PatchReplacement? TryFindIndependentSubstringReplacement(
        IReadOnlyList<string> originalLines,
        PatchIndependentReplacementGroup group,
        PatchHunk hunk)
    {
        if (group.BeforeLines.Count != 1 ||
            group.AfterLines.Count != 1 ||
            string.IsNullOrEmpty(group.BeforeLines[0]))
        {
            return null;
        }

        string oldText = group.BeforeLines[0];
        string newText = group.AfterLines[0];
        List<int> matches = [];

        for (int index = 0; index < originalLines.Count; index++)
        {
            if (originalLines[index].Contains(oldText, StringComparison.Ordinal))
            {
                matches.Add(index);
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                "Could not apply the requested patch because an independent text replacement matched multiple lines. " +
                $"Candidate starting lines: {DescribeCandidateLines(matches)}.");
        }

        int matchIndex = matches[0];
        string replacementLine = originalLines[matchIndex]
            .Replace(oldText, newText, StringComparison.Ordinal);

        return new PatchReplacement(
            matchIndex,
            OldLineCount: 1,
            [replacementLine],
            hunk);
    }

    private static IReadOnlyList<PatchIndependentReplacementGroup> BuildIndependentReplacementGroups(
        PatchHunk hunk)
    {
        List<PatchIndependentReplacementGroup> groups = [];
        int index = 0;

        while (index < hunk.Lines.Count)
        {
            while (index < hunk.Lines.Count &&
                   hunk.Lines[index].Kind == PatchLineKind.Context)
            {
                index++;
            }

            if (index >= hunk.Lines.Count)
            {
                break;
            }

            if (hunk.Lines[index].Kind != PatchLineKind.Removal)
            {
                return [];
            }

            List<string> beforeLines = [];
            while (index < hunk.Lines.Count &&
                   hunk.Lines[index].Kind == PatchLineKind.Removal)
            {
                beforeLines.Add(hunk.Lines[index].Text);
                index++;
            }

            if (index >= hunk.Lines.Count ||
                hunk.Lines[index].Kind != PatchLineKind.Addition)
            {
                return [];
            }

            List<string> afterLines = [];
            while (index < hunk.Lines.Count &&
                   hunk.Lines[index].Kind == PatchLineKind.Addition)
            {
                afterLines.Add(hunk.Lines[index].Text);
                index++;
            }

            if (beforeLines.Count == 0 || afterLines.Count == 0)
            {
                return [];
            }

            groups.Add(new PatchIndependentReplacementGroup(
                beforeLines,
                afterLines));
        }

        return groups;
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

    private static int FindFirstSequenceMatch(
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

    private static PatchSequenceSearchResult FindUniqueSequenceMatch(
        IReadOnlyList<string> source,
        IReadOnlyList<string> target,
        int startIndex,
        bool endOfFile)
    {
        if (target.Count == 0)
        {
            return new PatchSequenceSearchResult(
                PatchSequenceSearchStatus.NotFound,
                -1,
                string.Empty,
                []);
        }

        (string Name, Func<string, string, bool> Comparer)[] comparers =
        [
            ("exact text", static (left, right) => string.Equals(left, right, StringComparison.Ordinal)),
            ("trimmed line endings", static (left, right) => string.Equals(left.TrimEnd(), right.TrimEnd(), StringComparison.Ordinal)),
            ("trimmed whitespace", static (left, right) => string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal)),
            ("normalized punctuation", static (left, right) => string.Equals(
                NormalizePatchMatchText(left.Trim()),
                NormalizePatchMatchText(right.Trim()),
                StringComparison.Ordinal))
        ];

        foreach ((string name, Func<string, string, bool> comparer) in comparers)
        {
            int[] matches = FindAllSequenceMatches(source, target, startIndex, endOfFile, comparer);
            if (matches.Length == 0)
            {
                continue;
            }

            if (matches.Length == 1)
            {
                return new PatchSequenceSearchResult(
                    PatchSequenceSearchStatus.Matched,
                    matches[0],
                    name,
                    matches);
            }

            return new PatchSequenceSearchResult(
                PatchSequenceSearchStatus.Ambiguous,
                -1,
                name,
                matches);
        }

        return new PatchSequenceSearchResult(
            PatchSequenceSearchStatus.NotFound,
            -1,
            string.Empty,
            []);
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

    private static int[] FindAllSequenceMatches(
        IReadOnlyList<string> source,
        IReadOnlyList<string> target,
        int startIndex,
        bool endOfFile,
        Func<string, string, bool> comparer)
    {
        List<int> matches = [];
        if (endOfFile)
        {
            int fromEndIndex = source.Count - target.Count;
            if (fromEndIndex >= Math.Max(0, startIndex) &&
                SequenceMatches(source, target, fromEndIndex, comparer))
            {
                matches.Add(fromEndIndex);
                return matches.ToArray();
            }
        }

        for (int index = Math.Max(0, startIndex); index <= source.Count - target.Count; index++)
        {
            if (SequenceMatches(source, target, index, comparer))
            {
                matches.Add(index);
            }
        }

        return matches.ToArray();
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
        return DecodeCommonJsonUnicodeEscapes(value)
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

    private static string DecodeCommonJsonUnicodeEscapes(string value)
    {
        if (value.IndexOf('\\', StringComparison.Ordinal) < 0)
        {
            return value;
        }

        StringBuilder? builder = null;
        for (int index = 0; index < value.Length; index++)
        {
            if (TryDecodeCommonJsonUnicodeEscape(value, index, out char decoded))
            {
                if (builder is null)
                {
                    builder = new StringBuilder(value.Length);
                    builder.Append(value, 0, index);
                }

                builder.Append(decoded);
                index += 5;
                continue;
            }

            builder?.Append(value[index]);
        }

        return builder is null
            ? value
            : builder.ToString();
    }

    private static bool TryDecodeCommonJsonUnicodeEscape(
        string value,
        int index,
        out char decoded)
    {
        decoded = default;
        if (index + 5 >= value.Length ||
            value[index] != '\\' ||
            value[index + 1] is not ('u' or 'U'))
        {
            return false;
        }

        int codePoint = 0;
        for (int offset = 2; offset < 6; offset++)
        {
            int digit = FromHexDigit(value[index + offset]);
            if (digit < 0)
            {
                return false;
            }

            codePoint = (codePoint << 4) + digit;
        }

        decoded = codePoint switch
        {
            0x0022 => '"',
            0x0026 => '&',
            0x0027 => '\'',
            0x002F => '/',
            0x003C => '<',
            0x003D => '=',
            0x003E => '>',
            0x0060 => '`',
            _ => default
        };

        return decoded != default;
    }

    private static int FromHexDigit(char value)
    {
        if (value is >= '0' and <= '9')
        {
            return value - '0';
        }

        if (value is >= 'a' and <= 'f')
        {
            return value - 'a' + 10;
        }

        if (value is >= 'A' and <= 'F')
        {
            return value - 'A' + 10;
        }

        return -1;
    }

    private static PatchRetryMatch? TryFindRetryMatch(
        IReadOnlyList<string> originalLines,
        PatchHunk hunk,
        int searchStart,
        bool endOfFile)
    {
        (string[] BeforeLines, string[] AfterLines)[] retryWindows =
        [
            BuildRetryWindow(hunk, includeLeadingAndTrailingContext: false),
            BuildRetryWindow(hunk, removalsOnly: true)
        ];

        foreach ((string[] beforeRetry, string[] afterRetry) in retryWindows)
        {
            if (beforeRetry.Length == 0)
            {
                continue;
            }

            PatchSequenceSearchResult retryMatch = FindUniqueSequenceMatch(
                originalLines,
                beforeRetry,
                searchStart,
                endOfFile);
            if (retryMatch.Status == PatchSequenceSearchStatus.Matched)
            {
                return new PatchRetryMatch(
                    retryMatch.StartIndex,
                    beforeRetry.Length,
                    afterRetry,
                    retryMatch.MatchStyle);
            }
        }

        return null;
    }

    private static (string[] BeforeLines, string[] AfterLines) BuildRetryWindow(
        PatchHunk hunk,
        bool includeLeadingAndTrailingContext = true,
        bool removalsOnly = false)
    {
        IReadOnlyList<PatchLine> workingLines = hunk.Lines;
        if (!includeLeadingAndTrailingContext)
        {
            int firstChangedIndex = -1;
            int lastChangedIndex = -1;
            for (int index = 0; index < workingLines.Count; index++)
            {
                if (workingLines[index].Kind != PatchLineKind.Context)
                {
                    firstChangedIndex = index;
                    break;
                }
            }

            for (int index = workingLines.Count - 1; index >= 0; index--)
            {
                if (workingLines[index].Kind != PatchLineKind.Context)
                {
                    lastChangedIndex = index;
                    break;
                }
            }

            if (firstChangedIndex >= 0 && lastChangedIndex >= firstChangedIndex)
            {
                workingLines = workingLines
                    .Skip(firstChangedIndex)
                    .Take(lastChangedIndex - firstChangedIndex + 1)
                    .ToArray();
            }
        }

        string[] beforeLines = removalsOnly
            ? workingLines
                .Where(static line => line.Kind == PatchLineKind.Removal)
                .Select(static line => line.Text)
                .ToArray()
            : workingLines
                .Where(static line => line.Kind is PatchLineKind.Context or PatchLineKind.Removal)
                .Select(static line => line.Text)
                .ToArray();
        string[] afterLines = removalsOnly
            ? workingLines
                .Where(static line => line.Kind == PatchLineKind.Addition)
                .Select(static line => line.Text)
                .ToArray()
            : workingLines
                .Where(static line => line.Kind is PatchLineKind.Context or PatchLineKind.Addition)
                .Select(static line => line.Text)
                .ToArray();
        return (beforeLines, afterLines);
    }

    private static string DescribeCandidateLines(
        IReadOnlyList<int> candidateIndexes)
    {
        return string.Join(
            ", ",
            candidateIndexes
                .Take(4)
                .Select(static index => (index + 1).ToString(CultureInfo.InvariantCulture)));
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

    private static bool IsLargeContent(string? content)
    {
        return content?.Length > ContentPreviewThresholdChars;
    }


    private static string[] GetTrackedPatchPaths(PatchDocument document)
    {
        HashSet<string> paths = new(WorkspacePath.GetPathComparer());
        foreach (PatchOperation op in document.Operations)
        {
            paths.Add(op.Path);
            if (op.MoveToPath is not null)
            {
                paths.Add(op.MoveToPath);
            }
        }
        return paths.ToArray();
    }

    private async Task<string[]> FilterLargeFilePathsAsync(
        string[] paths,
        CancellationToken cancellationToken)
    {
        List<string> filtered = [];
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string fullPath = ResolveWorkspacePath(path, directoryRequired: false, fileRequired: false);
                if (!File.Exists(fullPath) || !TryGetFileLength(fullPath, out long length) || length <= LargeFileThresholdBytes)
                {
                    filtered.Add(path);
                }
            }
            catch
            {
                filtered.Add(path);
            }
        }
        return filtered.ToArray();
    }

    private static FileWritePreview BuildFileWritePreview(
        string? previousContent,
        string currentContent)
    {
        if (IsLargeContent(previousContent) || IsLargeContent(currentContent))
        {
            string[] prevLines = SplitLines(previousContent);
            string[] currLines = SplitLines(currentContent);
            int added = Math.Max(0, currLines.Length - (prevLines?.Length ?? 0));
            int removed = Math.Max(0, (prevLines?.Length ?? 0) - currLines.Length);
            return new FileWritePreview(added, removed, [], 0);
        }

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

    private readonly record struct PatchRetryMatch(
        int StartIndex,
        int OldLineCount,
        IReadOnlyList<string> NewLines,
        string MatchStyle);
    private readonly record struct PatchIndependentReplacementGroup(
        IReadOnlyList<string> BeforeLines,
        IReadOnlyList<string> AfterLines);

    private readonly record struct PatchSequenceSearchResult(
        PatchSequenceSearchStatus Status,
        int StartIndex,
        string MatchStyle,
        IReadOnlyList<int> CandidateStartIndexes);

    private enum PatchOperationKind
    {
        Add = 0,
        Delete = 1,
        Update = 2
    }

    private enum PatchSequenceSearchStatus
    {
        NotFound = 0,
        Matched = 1,
        Ambiguous = 2
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
