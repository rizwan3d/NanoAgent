using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IInteractiveReasoningSelectionService
{
    Task<ReplCommandResult> SelectAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
