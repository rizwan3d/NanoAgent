using NanoAgent.Application.Models;
using NanoAgent.Application.Commands;

namespace NanoAgent.Application.Commands;

public interface IReplCommandHandler
{
    string CommandName { get; }

    string Description { get; }

    string Usage { get; }

    Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken);
}
