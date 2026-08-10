using StemCode.Application.Models;

namespace StemCode.Application.Commands;

public interface IReplCommandDispatcher
{
    Task<ReplCommandResult> DispatchAsync(
        ParsedReplCommand command,
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
