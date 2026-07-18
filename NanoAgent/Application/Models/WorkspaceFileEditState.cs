using System.Text.Json.Serialization;

namespace NanoAgent.Application.Models;

public sealed class WorkspaceFileEditState
{
    public WorkspaceFileEditState(
        string path,
        bool exists,
        string? content,
        string? contentHash = null,
        string? encoding = null,
        string? newLine = null,
        string? contentBackupPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (exists &&
            content is null &&
            string.IsNullOrWhiteSpace(contentHash) &&
            string.IsNullOrWhiteSpace(contentBackupPath))
        {
            throw new ArgumentException(
                "Existing file states must include content, a content hash, or a backup path for tracked restores.",
                nameof(content));
        }

        Path = path.Trim();
        Exists = exists;
        Content = content;
        ContentHash = string.IsNullOrWhiteSpace(contentHash) ? null : contentHash;
        Encoding = string.IsNullOrWhiteSpace(encoding) ? null : encoding.Trim();
        NewLine = string.IsNullOrEmpty(newLine)
            ? null
            : newLine.Replace("\r\n", "\n", StringComparison.Ordinal) == "\n" && newLine.Contains('\r')
                ? "\r\n"
                : "\n";
        ContentBackupPath = string.IsNullOrWhiteSpace(contentBackupPath)
            ? null
            : contentBackupPath.Trim();
    }

    public string? Content { get; }

    /// <summary>
    /// Temporary on-disk backup used when rollback must restore the original file bytes exactly.
    /// </summary>
    public string? ContentBackupPath { get; }

    /// <summary>
    /// SHA256 content hash, stored instead of full <see cref="Content"/> for large files
    /// to avoid holding large content in memory. Used for stale-content verification.
    /// </summary>
    public string? ContentHash { get; }

    public string? Encoding { get; }

    public bool Exists { get; }

    public string? NewLine { get; }

    public string Path { get; }
}
