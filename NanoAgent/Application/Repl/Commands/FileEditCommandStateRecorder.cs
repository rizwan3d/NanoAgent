using NanoAgent.Application.Models;

namespace NanoAgent.Application.Repl.Commands;

internal static class FileEditCommandStateRecorder
{
    public static void Record(
        ReplSessionContext session,
        string commandName,
        string summary,
        WorkspaceFileEditTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(transaction);

        DateTimeOffset recordedAtUtc = DateTimeOffset.UtcNow;
        string[] paths = transaction.BeforeStates
            .Select(static state => state.Path)
            .Concat(transaction.AfterStates.Select(static state => state.Path))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        session.RecordEditContext(new SessionEditContext(
            recordedAtUtc,
            $"{commandName} ({transaction.Description})",
            paths,
            0,
            0));

        foreach (string path in paths)
        {
            session.RecordFileContext(new SessionFileContext(
                path,
                "edited",
                recordedAtUtc,
                $"{summary}: {transaction.Description}."));
        }
    }
}
