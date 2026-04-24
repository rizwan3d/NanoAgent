using NanoAgent.Application.Models;
using NanoAgent.CLI.Commands;

namespace NanoAgent.CLI.Commands;

public interface IReplCommandHandler
{
    string CommandName { get; }

    string Description { get; }

    string Usage { get; }

    Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken);
}
