using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IInteractiveModelSelectionService
{
    Task<ReplCommandResult> SelectAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
