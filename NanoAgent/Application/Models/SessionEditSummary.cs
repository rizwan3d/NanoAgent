namespace NanoAgent.Application.Models;

/// <summary>Per-file rollup of how many lines the conversation added/removed, for the end-of-conversation summary table.</summary>
public sealed record FileEditSummary(
    string DisplayPath,
    string AbsolutePath,
    int AddedLineCount,
    int RemovedLineCount,
    int EditCount,
    string Action = "Edited");

public static class SessionEditSummary
{
    /// <summary>
    /// Collapses the recorded edit history into one row per file (summed +/- and edit count),
    /// ordered by most-changed first. <paramref name="resolveAbsolute"/> turns a workspace-relative
    /// path into an absolute filesystem path for clickable links.
    /// </summary>
    public static IReadOnlyList<FileEditSummary> Build(
        IEnumerable<SessionEditContext> edits,
        Func<string, string> resolveAbsolute)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(resolveAbsolute);

        Dictionary<string, FileEditSummary> byPath = new(StringComparer.OrdinalIgnoreCase);
        // Tracks how to label each file: created if it was ever added this conversation,
        // deleted if its last operation removed it, otherwise edited.
        Dictionary<string, (bool Created, string Last)> actionByPath = new(StringComparer.OrdinalIgnoreCase);

        foreach (SessionEditContext edit in edits)
        {
            string action = DeriveAction(edit.Description);

            foreach (string rawPath in edit.Paths ?? [])
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                // A rename is recorded as "old -> new"; attribute the change to the destination.
                string display = rawPath.Contains("->", StringComparison.Ordinal)
                    ? rawPath[(rawPath.LastIndexOf("->", StringComparison.Ordinal) + 2)..].Trim()
                    : rawPath.Trim();

                FileEditSummary existing = byPath.TryGetValue(display, out FileEditSummary? found)
                    ? found
                    : new FileEditSummary(display, resolveAbsolute(display), 0, 0, 0);

                byPath[display] = existing with
                {
                    AddedLineCount = existing.AddedLineCount + Math.Max(0, edit.AddedLineCount),
                    RemovedLineCount = existing.RemovedLineCount + Math.Max(0, edit.RemovedLineCount),
                    EditCount = existing.EditCount + 1,
                };

                (bool created, string _) = actionByPath.TryGetValue(display, out (bool, string) prior)
                    ? prior
                    : (false, action);
                actionByPath[display] = (created || action == "Created", action);
            }
        }

        return byPath.Values
            .Select(f => f with { Action = ResolveAction(actionByPath[f.DisplayPath]) })
            .OrderByDescending(static f => f.AddedLineCount + f.RemovedLineCount)
            .ThenBy(static f => f.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // Edit descriptions are formatted by SessionStateToolRecorder, e.g. "file_write created (x)",
    // "apply_patch (add x)", "file_delete (x)". Anything else is a plain edit.
    private static string DeriveAction(string? description)
    {
        string d = description ?? string.Empty;
        if (d.StartsWith("file_delete", StringComparison.OrdinalIgnoreCase)) return "Deleted";
        if (d.StartsWith("file_write created", StringComparison.OrdinalIgnoreCase)) return "Created";
        if (d.StartsWith("apply_patch (add", StringComparison.OrdinalIgnoreCase)) return "Created";
        if (d.StartsWith("apply_patch (delete", StringComparison.OrdinalIgnoreCase)) return "Deleted";
        return "Edited";
    }

    private static string ResolveAction((bool Created, string Last) state)
        => state.Last == "Deleted" ? "Deleted"
            : state.Created ? "Created"
            : "Edited";
}
