namespace NanoAgent.Application.Models;

public sealed record WorkspaceFileMetadata(
    FileAttributes Attributes,
    DateTime CreationTimeUtc,
    DateTime LastAccessTimeUtc,
    DateTime LastWriteTimeUtc);
