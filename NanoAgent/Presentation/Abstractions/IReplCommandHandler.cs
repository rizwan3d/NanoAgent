using NanoAgent.Application.Models;
using NanoAgent.Presentation.Abstractions;

namespace NanoAgent.Presentation.Abstractions;

public interface IReplCommandHandler
{
    string CommandName { get; }

    string Description { get; }

    string Usage { get; }

    Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken);
}
