using NanoAgent.Application.Models;
using NanoAgent.Application.Commands;

namespace NanoAgent.Application.Commands;

public interface IReplCommandDispatcher
{
    Task<ReplCommandResult> DispatchAsync(
        ParsedReplCommand command,
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
