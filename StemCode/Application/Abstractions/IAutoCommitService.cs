using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IAutoCommitService
{
    Task TryAutoCommitAsync(
        ReplSessionContext session,
        IReadOnlyList<SessionEditContext> newEdits,
        CancellationToken cancellationToken);
}
