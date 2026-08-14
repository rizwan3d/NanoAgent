namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceDirectoryListResult(
    string Path,
    IReadOnlyList<WorkspaceDirectoryEntry> Entries);
