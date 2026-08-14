using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IInteractiveModelSelectionService
{
    Task<ReplCommandResult> SelectAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
