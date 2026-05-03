namespace NanoAgent.Application.Tools.Models;

public sealed record CodebaseIndexBuildResult(
    string IndexPath,
    DateTimeOffset BuiltAtUtc,
    int IndexedFileCount,
    int AddedFileCount,
    int UpdatedFileCount,
    int RemovedFileCount,
    int ReusedFileCount,
    int SkippedFileCount,
    long DurationMilliseconds);

public sealed record CodebaseIndexStatusResult(
    string IndexPath,
    bool Exists,
    bool IsStale,
    DateTimeOffset? BuiltAtUtc,
    int IndexedFileCount,
    int WorkspaceFileCount,
    int NewFileCount,
    int ChangedFileCount,
    int DeletedFileCount,
    int SkippedFileCount,
    IReadOnlyList<string> SampleNewFiles,
    IReadOnlyList<string> SampleChangedFiles,
    IReadOnlyList<string> SampleDeletedFiles);

public sealed record CodebaseIndexSearchResult(
    string Query,
    string IndexPath,
    bool IndexWasUpdated,
    int IndexedFileCount,
    IReadOnlyList<CodebaseIndexSearchMatch> Matches);

public sealed record CodebaseIndexSearchMatch(
    string Path,
    string Language,
    double Score,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<CodebaseIndexSnippet> Snippets);

public sealed record CodebaseIndexSnippet(
    int LineNumber,
    string Text);

public sealed record CodebaseIndexListResult(
    string IndexPath,
    int TotalIndexedFileCount,
    int ReturnedFileCount,
    IReadOnlyList<string> Files);
