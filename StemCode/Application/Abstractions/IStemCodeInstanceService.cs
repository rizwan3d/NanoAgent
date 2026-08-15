using System.Collections.Generic;

namespace StemCode.Application.Abstractions;

/// <summary>
/// Describes a running StemCode CLI process that is not the current session.
/// </summary>
public sealed record RunningStemCodeInstance(
    int ProcessId,
    string ProcessName);

/// <summary>
/// Detects and terminates other running StemCode CLI sessions so an in-place
/// update can replace the currently executing binary without file-lock errors.
/// </summary>
public interface IStemCodeInstanceService
{
    /// <summary>
    /// Returns the running StemCode CLI instances other than the current process.
    /// Best-effort: an enumeration failure returns an empty list rather than throwing.
    /// </summary>
    Task<IReadOnlyList<RunningStemCodeInstance>> GetOtherRunningInstancesAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Terminates a single running instance. Best-effort: failures such as the
    /// target having already exited are swallowed so an update can proceed.
    /// </summary>
    Task TerminateAsync(RunningStemCodeInstance instance, CancellationToken cancellationToken);
}
