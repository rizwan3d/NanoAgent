using NanoAgent.Application.Models;
using NanoAgent.Presentation.Abstractions;

namespace NanoAgent.Presentation.Abstractions;

public interface IReplCommandDispatcher
{
    Task<ReplCommandResult> DispatchAsync(
        ParsedReplCommand command,
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
