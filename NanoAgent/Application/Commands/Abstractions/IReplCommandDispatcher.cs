using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

public interface IReplCommandDispatcher
{
    Task<ReplCommandResult> DispatchAsync(
        ParsedReplCommand command,
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
