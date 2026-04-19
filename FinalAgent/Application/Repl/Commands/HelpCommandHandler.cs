using FinalAgent.Application.Abstractions;
using FinalAgent.Application.Models;

namespace FinalAgent.Application.Repl.Commands;

internal sealed class HelpCommandHandler : IReplCommandHandler
{
    public string CommandName => "help";

    public string Description => "Show the available shell commands.";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const string HelpText =
            "Available commands:\n" +
            "/help - Show the available shell commands.\n" +
            "/exit - Exit the interactive shell.";

        return Task.FromResult(ReplCommandResult.Continue(HelpText));
    }
}
