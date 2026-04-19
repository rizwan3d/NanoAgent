using FinalAgent.Application.Abstractions;
using FinalAgent.Application.Models;

namespace FinalAgent.Application.Repl.Services;

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
        string commandText,
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        string trimmedInput = commandText.Trim();
        if (!trimmedInput.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("REPL commands must start with '/'.", nameof(commandText));
        }

        string commandBody = trimmedInput[1..].Trim();
        if (string.IsNullOrWhiteSpace(commandBody))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                "Command name cannot be empty. Type /help to see the available commands.",
                ReplFeedbackKind.Error));
        }

        string commandName;
        string arguments;

        int firstSpaceIndex = commandBody.IndexOf(' ');
        if (firstSpaceIndex < 0)
        {
            commandName = commandBody;
            arguments = string.Empty;
        }
        else
        {
            commandName = commandBody[..firstSpaceIndex];
            arguments = commandBody[(firstSpaceIndex + 1)..].Trim();
        }

        if (!_commandHandlers.TryGetValue(commandName, out IReplCommandHandler? handler))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                $"Unknown command '/{commandName}'. Type /help to see the available commands.",
                ReplFeedbackKind.Error));
        }

        return handler.ExecuteAsync(
            new ReplCommandContext(
                commandName,
                arguments,
                trimmedInput,
                session),
            cancellationToken);
    }
}
