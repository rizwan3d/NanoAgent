namespace NanoAgent.Application.Models;

public sealed class WorkspaceFileEditState
{
    public WorkspaceFileEditState(
        string path,
        bool exists,
        string? content,
        string? contentHash = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (exists && content is null && string.IsNullOrWhiteSpace(contentHash))
        {
            throw new ArgumentException(
                "Existing file states must include content or a content hash for large-file tracking.",
                nameof(content));
        }

        Path = path.Trim();
        Exists = exists;
        Content = content;
        ContentHash = string.IsNullOrWhiteSpace(contentHash) ? null : contentHash;
    }

    public string? Content { get; }

    /// <summary>
    /// SHA256 content hash, stored instead of full <see cref="Content"/> for large files
    /// to avoid holding large content in memory. Used for stale-content verification.
    /// </summary>
    public string? ContentHash { get; }

    public bool Exists { get; }

    public string Path { get; }
}
