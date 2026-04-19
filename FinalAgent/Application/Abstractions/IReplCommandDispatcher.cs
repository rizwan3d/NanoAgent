using FinalAgent.Application.Models;

namespace FinalAgent.Application.Abstractions;

public interface IReplCommandDispatcher
{
    Task<ReplCommandResult> DispatchAsync(
        string commandText,
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
