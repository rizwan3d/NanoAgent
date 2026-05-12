namespace NanoAgent.Application.Models;

/// <summary>
/// Accumulated context from completed sections within a session.
/// This enables reuse of knowledge across sections.
/// </summary>
public sealed class SessionContext
{
    public static SessionContext Empty { get; } = new([], [], [], []);

    public SessionContext(
        IReadOnlyList<SessionFileContext> files,
        IReadOnlyList<SessionEditContext> edits,
        IReadOnlyList<SessionTerminalCommand> terminalHistory,
        IReadOnlyList<string> completedSectionSummaries)
    {
        Files = files ?? [];
        Edits = edits ?? [];
        TerminalHistory = terminalHistory ?? [];
        CompletedSectionSummaries = completedSectionSummaries ?? [];
    }

    public IReadOnlyList<SessionFileContext> Files { get; }

    public IReadOnlyList<SessionEditContext> Edits { get; }

    public IReadOnlyList<SessionTerminalCommand> TerminalHistory { get; }

    /// <summary>
    /// Brief summaries of what was done in completed sections.
    /// </summary>
    public IReadOnlyList<string> CompletedSectionSummaries { get; }

    public bool IsEmpty => Files.Count == 0 &&
                           Edits.Count == 0 &&
                           TerminalHistory.Count == 0 &&
                           CompletedSectionSummaries.Count == 0;

    public SessionContext Merge(SessionContext other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.IsEmpty)
        {
            return this;
        }

        List<SessionFileContext> mergedFiles = new(Files);
        mergedFiles.AddRange(other.Files);

        List<SessionEditContext> mergedEdits = new(Edits);
        mergedEdits.AddRange(other.Edits);

        List<SessionTerminalCommand> mergedTerminal = new(TerminalHistory);
        mergedTerminal.AddRange(other.TerminalHistory);

        List<string> mergedSummaries = new(CompletedSectionSummaries);
        mergedSummaries.AddRange(other.CompletedSectionSummaries);

        return new SessionContext(
            mergedFiles,
            mergedEdits,
            mergedTerminal,
            mergedSummaries);
    }
}
