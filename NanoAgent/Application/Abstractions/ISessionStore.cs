using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface ISessionStore
{
    Task<SessionRecord?> LoadAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task SaveAsync(
        SessionRecord session,
        CancellationToken cancellationToken);
}
