using FinalAgent.Application.Models;

namespace FinalAgent.Application.Abstractions;

public interface IReplCommandHandler
{
    string CommandName { get; }

    string Description { get; }

    Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken);
}
