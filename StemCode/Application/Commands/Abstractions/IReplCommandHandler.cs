using StemCode.Application.Models;

namespace StemCode.Application.Commands;

public interface IReplCommandHandler
{
    string CommandName { get; }

    string Description { get; }

    string Usage { get; }

    Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken);
}
