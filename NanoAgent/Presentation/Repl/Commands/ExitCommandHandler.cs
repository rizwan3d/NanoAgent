using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Presentation.Abstractions;

namespace NanoAgent.Presentation.Repl.Commands;

internal sealed class ExitCommandHandler : IReplCommandHandler
{
    public string CommandName => "exit";

    public string Description => "Exit the interactive shell.";

    public string Usage => "/exit";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ReplCommandResult.Exit("Exiting NanoAgent."));
    }
}
