using FinalAgent.Application.Abstractions;
using FinalAgent.Application.Models;

namespace FinalAgent.Application.Repl.Commands;

internal sealed class ExitCommandHandler : IReplCommandHandler
{
    public string CommandName => "exit";

    public string Description => "Exit the interactive shell.";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ReplCommandResult.Exit("Exiting FinalAgent."));
    }
}
