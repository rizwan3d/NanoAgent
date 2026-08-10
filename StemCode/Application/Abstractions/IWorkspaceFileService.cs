using StemCode.Application.Models;
using StemCode.Application.Tools.Models;

namespace StemCode.Application.Abstractions;

public interface IWorkspaceFileService
{
    Task ApplyFileEditStatesAsync(
        IReadOnlyList<WorkspaceFileEditState> states,
        CancellationToken cancellationToken);

    Task<WorkspaceApplyPatchResult> ApplyPatchAsync(
        string patch,
        CancellationToken cancellationToken);

    Task<WorkspaceApplyPatchExecutionResult> ApplyPatchWithTrackingAsync(
        string patch,
        CancellationToken cancellationToken);

    void ReleaseTrackedFileEditTransactions(
        IReadOnlyList<WorkspaceFileEditTransaction> transactions);

    void RetainTrackedFileEditTransaction(
        WorkspaceFileEditTransaction transaction);

    Task<WorkspaceDirectoryListResult> ListDirectoryAsync(
        string? path,
        bool recursive,
        CancellationToken cancellationToken);

    Task<WorkspaceFileDeleteResult> DeleteFileAsync(
        string path,
        CancellationToken cancellationToken);

    Task<WorkspaceFileDeleteExecutionResult> DeleteFileWithTrackingAsync(
        string path,
        CancellationToken cancellationToken);

    Task<WorkspaceFileInsertResult> InsertContentAsync(
        string path,
        int line,
        string content,
        CancellationToken cancellationToken);

    Task<WorkspaceFileInsertExecutionResult> InsertContentWithTrackingAsync(
        string path,
        int line,
        string content,
        CancellationToken cancellationToken);

    Task<WorkspaceFileReadResult> ReadFileAsync(
        string path,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<WorkspaceFileSearchResult> SearchFilesAsync(
        WorkspaceFileSearchRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceSearchAndReplaceResult> SearchAndReplaceAsync(
        string path,
        string search,
        string replace,
        bool useRegex,
        bool caseSensitive,
        CancellationToken cancellationToken);

    Task<WorkspaceSearchAndReplaceExecutionResult> SearchAndReplaceWithTrackingAsync(
        string path,
        string search,
        string replace,
        bool useRegex,
        bool caseSensitive,
        CancellationToken cancellationToken);

    Task<WorkspaceTextSearchResult> SearchTextAsync(
        WorkspaceTextSearchRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceFileWriteResult> WriteFileAsync(
        string path,
        string content,
        bool overwrite,
        CancellationToken cancellationToken);

    Task<WorkspaceFileWriteExecutionResult> WriteFileWithTrackingAsync(
        string path,
        string content,
        bool overwrite,
        CancellationToken cancellationToken);
}
