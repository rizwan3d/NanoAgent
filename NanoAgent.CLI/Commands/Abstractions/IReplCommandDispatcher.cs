using NanoAgent.Application.Models;
using NanoAgent.CLI.Commands;

namespace NanoAgent.CLI.Commands;

public interface IReplCommandDispatcher
{
    Task<ReplCommandResult> DispatchAsync(
        ParsedReplCommand command,
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
