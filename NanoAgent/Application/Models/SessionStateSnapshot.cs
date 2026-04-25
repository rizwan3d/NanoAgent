using System.Text.Json.Serialization;

namespace NanoAgent.Application.Models;

public sealed record SessionStateSnapshot(
    IReadOnlyList<SessionFileContext> Files,
    IReadOnlyList<SessionEditContext> Edits,
    IReadOnlyList<SessionTerminalCommand> TerminalHistory,
    string WorkingDirectory = ".")
{
    public static SessionStateSnapshot Empty { get; } = new([], [], []);

    [JsonIgnore]
    public bool IsEmpty => (Files?.Count ?? 0) == 0 &&
                           (Edits?.Count ?? 0) == 0 &&
                           (TerminalHistory?.Count ?? 0) == 0 &&
                           IsRootWorkingDirectory(WorkingDirectory);

    private static bool IsRootWorkingDirectory(string? workingDirectory)
    {
        return string.IsNullOrWhiteSpace(workingDirectory) ||
            string.Equals(workingDirectory.Trim(), ".", StringComparison.Ordinal);
    }
}
