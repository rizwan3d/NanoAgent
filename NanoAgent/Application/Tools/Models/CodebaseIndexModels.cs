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
    long DurationMilliseconds,
    CodebaseIndexStats Stats,
    IReadOnlyList<string> Warnings);

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
    CodebaseIndexStats Stats,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> SampleNewFiles,
    IReadOnlyList<string> SampleChangedFiles,
    IReadOnlyList<string> SampleDeletedFiles);

public sealed record CodebaseIndexSearchResult(
    string Query,
    string IndexPath,
    bool IndexWasUpdated,
    int IndexedFileCount,
    CodebaseIndexStats Stats,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CodebaseIndexSearchMatch> Matches);

public sealed record CodebaseIndexSearchMatch(
    string Path,
    string Language,
    double Score,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<CodebaseIndexSemanticSymbol> SemanticSymbols,
    IReadOnlyList<CodebaseIndexDependency> Dependencies,
    IReadOnlyList<CodebaseIndexCallEdge> OutgoingCalls,
    IReadOnlyList<CodebaseIndexCallEdge> IncomingCalls,
    IReadOnlyList<string> Owners,
    IReadOnlyList<CodebaseIndexSnippet> Snippets);

public sealed record CodebaseIndexSemanticSymbol(
    string Name,
    string Kind,
    string? ContainerName,
    string? Signature,
    int StartLine,
    int EndLine);

public sealed record CodebaseIndexDependency(
    string Kind,
    string Target,
    bool IsWorkspaceLocal,
    IReadOnlyList<string> ResolvedPaths);

public sealed record CodebaseIndexCallEdge(
    string CallerSymbol,
    string CallerPath,
    string CalleeSymbol,
    string? CalleePath,
    int LineNumber,
    bool IsResolved);

public sealed record CodebaseIndexSnippet(
    int LineNumber,
    string Text);

public sealed record CodebaseIndexStats(
    int SemanticSymbolCount,
    int DependencyEdgeCount,
    int CallEdgeCount,
    int OwnedFileCount,
    int OwnershipRuleCount);

public sealed record CodebaseIndexListResult(
    string IndexPath,
    int TotalIndexedFileCount,
    int ReturnedFileCount,
    CodebaseIndexStats Stats,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Files);
