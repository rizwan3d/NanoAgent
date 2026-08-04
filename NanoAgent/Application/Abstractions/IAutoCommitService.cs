using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IAutoCommitService
{
    Task TryAutoCommitAsync(
        ReplSessionContext session,
        IReadOnlyList<SessionEditContext> newEdits,
        CancellationToken cancellationToken);
}
