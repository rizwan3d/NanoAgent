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
        string? contentBackupId = null,
        WorkspaceFileMetadata? originalMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (exists &&
            content is null &&
            string.IsNullOrWhiteSpace(contentHash) &&
            string.IsNullOrWhiteSpace(contentBackupId))
        {
            throw new ArgumentException(
                "Existing file states must include content, a content hash, or a backup ID for tracked restores.",
                nameof(content));
        }

        Path = path.Trim();
        Exists = exists;
        Content = content;
        ContentHash = string.IsNullOrWhiteSpace(contentHash) ? null : contentHash;
        Encoding = string.IsNullOrWhiteSpace(encoding) ? null : encoding.Trim();
        NewLine = NormalizeNewLine(newLine);
        ContentBackupId = string.IsNullOrWhiteSpace(contentBackupId)
            ? null
            : contentBackupId.Trim();
        OriginalMetadata = originalMetadata;
    }

    public string? Content { get; }

    /// <summary>
    /// Managed backup ID used when rollback must restore the original file bytes exactly.
    /// </summary>
    public string? ContentBackupId { get; }

    /// <summary>
    /// SHA256 content hash, stored instead of full <see cref="Content"/> for large files
    /// to avoid holding large content in memory. Used for stale-content verification.
    /// </summary>
    public string? ContentHash { get; }

    public string? Encoding { get; }

    public bool Exists { get; }

    public string? NewLine { get; }

    public WorkspaceFileMetadata? OriginalMetadata { get; }

    public string Path { get; }

    private static string? NormalizeNewLine(string? newLine)
    {
        return newLine switch
        {
            "\r\n" => "\r\n",
            "\r" => "\r",
            "\n" => "\n",
            _ => null
        };
    }
}
