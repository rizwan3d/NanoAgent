using NanoAgent.Application.Models;
namespace NanoAgent.Application.Abstractions;

public interface ISessionAppService
{
    Task<ReplSessionContext> CreateAsync(
        CreateSessionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new section within the same session, accumulating context
    /// from the completed section.
    /// </summary>
    Task<ReplSessionContext> CreateNewSectionInSessionAsync(
        ReplSessionContext currentSession,
        CancellationToken cancellationToken);

    void EnsureTitleGenerationStarted(
        ReplSessionContext session,
        string firstUserPrompt);

    Task<IReadOnlyList<SessionSummary>> ListAsync(
        CancellationToken cancellationToken);

    Task<ReplSessionContext> ResumeAsync(
        ResumeSessionRequest request,
        CancellationToken cancellationToken);

    Task SaveIfDirtyAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);

    Task StopAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
