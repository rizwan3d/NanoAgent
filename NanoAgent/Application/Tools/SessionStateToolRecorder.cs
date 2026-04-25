using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Tools;

internal static class SessionStateToolRecorder
{
    private const int MaxContentExcerptCharacters = 1_200;
    private const int MaxListedItems = 25;
    private const int MaxSearchMatches = 12;
    private const int MaxPreviewLines = 8;
    private const int MaxTerminalOutputCharacters = 1_200;

    public static void RecordFileRead(
        ReplSessionContext session,
        WorkspaceFileReadResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);

        session.RecordFileContext(new SessionFileContext(
            result.Path,
            "read",
            DateTimeOffset.UtcNow,
            $"Read {result.CharacterCount} characters. Excerpt: {NormalizeForState(result.Content, MaxContentExcerptCharacters)}"));
    }

    public static void RecordDirectoryList(
        ReplSessionContext session,
        WorkspaceDirectoryListResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);

        string entries = result.Entries.Count == 0
            ? "(empty)"
            : string.Join(
                ", ",
                result.Entries
                    .Take(MaxListedItems)
                    .Select(static entry => $"{entry.EntryType}:{entry.Path}"));

        string suffix = result.Entries.Count > MaxListedItems
            ? $", ... {result.Entries.Count - MaxListedItems} more"
            : string.Empty;

        session.RecordFileContext(new SessionFileContext(
            result.Path,
            "listed directory",
            DateTimeOffset.UtcNow,
            $"Listed {result.Entries.Count} entries: {entries}{suffix}"));
    }

    public static void RecordFileSearch(
        ReplSessionContext session,
        WorkspaceFileSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);

        string matches = FormatItems(result.Matches, MaxSearchMatches);
        session.RecordFileContext(new SessionFileContext(
            result.Path,
            $"file search '{result.Query}'",
            DateTimeOffset.UtcNow,
            $"Found {result.Matches.Count} matching files: {matches}"));
    }

    public static void RecordTextSearch(
        ReplSessionContext session,
        WorkspaceTextSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);

        string matches = FormatItems(
            result.Matches.Select(static match =>
                $"{match.Path}:{match.LineNumber}: {NormalizeForState(match.LineText, 160)}"),
            MaxSearchMatches);

        session.RecordFileContext(new SessionFileContext(
            result.Path,
            $"text search '{result.Query}'",
            DateTimeOffset.UtcNow,
            $"Found {result.Matches.Count} text matches: {matches}"));
    }

    public static void RecordFileWrite(
        ReplSessionContext session,
        WorkspaceFileWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);

        string action = result.OverwroteExistingFile
            ? "updated"
            : "created";
        string preview = FormatPreview(result.PreviewLines, result.RemainingPreviewLineCount);

        session.RecordFileContext(new SessionFileContext(
            result.Path,
            "edited",
            DateTimeOffset.UtcNow,
            $"{action} by file_write (+{result.AddedLineCount} -{result.RemovedLineCount}). Preview: {preview}"));

        session.RecordEditContext(new SessionEditContext(
            DateTimeOffset.UtcNow,
            $"file_write ({result.Path})",
            [result.Path],
            result.AddedLineCount,
            result.RemovedLineCount));
    }

    public static void RecordFileDelete(
        ReplSessionContext session,
        WorkspaceFileDeleteResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);

        string preview = FormatPreview(result.PreviewLines, result.RemainingPreviewLineCount);
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;

        session.RecordFileContext(new SessionFileContext(
            result.Path,
            "deleted",
            observedAtUtc,
            $"deleted by file_delete (+{result.AddedLineCount} -{result.RemovedLineCount}). Preview: {preview}"));

        session.RecordEditContext(new SessionEditContext(
            observedAtUtc,
            $"file_delete ({result.Path})",
            [result.Path],
            result.AddedLineCount,
            result.RemovedLineCount));
    }

    public static void RecordApplyPatch(
        ReplSessionContext session,
        WorkspaceApplyPatchResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);

        List<string> paths = [];
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;

        foreach (WorkspaceApplyPatchFileResult file in result.Files)
        {
            string path = file.PreviousPath is null
                ? file.Path
                : $"{file.PreviousPath} -> {file.Path}";
            paths.Add(path);

            string preview = FormatPreview(file.PreviewLines, file.RemainingPreviewLineCount);
            session.RecordFileContext(new SessionFileContext(
                file.Path,
                "edited",
                observedAtUtc,
                $"{file.Operation} by apply_patch (+{file.AddedLineCount} -{file.RemovedLineCount}). Preview: {preview}"));
        }

        if (paths.Count == 0)
        {
            return;
        }

        session.RecordEditContext(new SessionEditContext(
            observedAtUtc,
            $"apply_patch ({result.FileCount} {(result.FileCount == 1 ? "file" : "files")})",
            paths,
            result.AddedLineCount,
            result.RemovedLineCount));
    }

    public static void RecordShellCommand(
        ReplSessionContext session,
        ShellCommandExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);

        session.RecordTerminalCommand(new SessionTerminalCommand(
            DateTimeOffset.UtcNow,
            result.Command,
            result.WorkingDirectory,
            result.ExitCode,
            NormalizeOptionalForState(result.StandardOutput, MaxTerminalOutputCharacters),
            NormalizeOptionalForState(result.StandardError, MaxTerminalOutputCharacters)));
    }

    private static string FormatPreview(
        IReadOnlyList<WorkspaceFileWritePreviewLine> previewLines,
        int remainingPreviewLineCount)
    {
        if (previewLines.Count == 0)
        {
            return "(no preview lines)";
        }

        string preview = string.Join(
            " | ",
            previewLines
                .Take(MaxPreviewLines)
                .Select(static line =>
                    $"{line.Kind}@{line.LineNumber}: {NormalizeForState(line.Text, 160)}"));

        return remainingPreviewLineCount > 0
            ? $"{preview} | ... {remainingPreviewLineCount} more"
            : preview;
    }

    private static string FormatItems(
        IEnumerable<string> values,
        int maxCount)
    {
        string[] selectedValues = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(maxCount + 1)
            .ToArray();

        if (selectedValues.Length == 0)
        {
            return "(none)";
        }

        string[] visibleValues = selectedValues.Take(maxCount).ToArray();
        string formatted = string.Join(", ", visibleValues);
        return selectedValues.Length > maxCount
            ? $"{formatted}, ... more"
            : formatted;
    }

    private static string? NormalizeOptionalForState(
        string? value,
        int maxCharacters)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeForState(value, maxCharacters);
    }

    private static string NormalizeForState(
        string value,
        int maxCharacters)
    {
        string normalized = SecretRedactor.Redact(value)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length <= maxCharacters)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxCharacters - 3)].TrimEnd() + "...";
    }
}
