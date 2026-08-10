using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IInteractiveReasoningSelectionService
{
    Task<ReplCommandResult> SelectAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
