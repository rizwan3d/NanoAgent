using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class ReplCommandDispatcher : IReplCommandDispatcher
{
    private readonly IReadOnlyDictionary<string, IReplCommandHandler> _commandHandlers;

    public ReplCommandDispatcher(IEnumerable<IReplCommandHandler> commandHandlers)
    {
        ArgumentNullException.ThrowIfNull(commandHandlers);

        _commandHandlers = commandHandlers.ToDictionary(
            handler => handler.CommandName,
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<ReplCommandResult> DispatchAsync(
        ParsedReplCommand command,
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.CommandName))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                "Command name cannot be empty. Type /help to see the available commands.",
                ReplFeedbackKind.Error));
        }

        if (!_commandHandlers.TryGetValue(command.CommandName, out IReplCommandHandler? handler))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                $"Unknown command '/{command.CommandName}'. Type /help to see the available commands.",
                ReplFeedbackKind.Error));
        }

        return handler.ExecuteAsync(
            new ReplCommandContext(
                command.CommandName,
                command.ArgumentText,
                command.Arguments,
                command.RawText,
                session),
            cancellationToken);
    }
}
