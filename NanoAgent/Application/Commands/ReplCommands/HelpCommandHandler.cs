using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class HelpCommandHandler : IReplCommandHandler
{
    private static readonly ReplCommandMetadata Metadata = ReplCommandCatalog.Get<HelpCommandHandler>();

    public string CommandName => Metadata.CommandName;

    public string Description => Metadata.Description;

    public string Usage => Metadata.Usage;

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string helpText =
            "Available commands:\n" +
            string.Join(
                '\n',
                ReplCommandCatalog.All.Select(static command =>
                    $"{command.Usage} - {command.Description}")) +
            "\n" +
            "!<shell-command> - Run a local shell command directly from the input box.\n" +
            "\nKeyboard shortcuts:\n" +
            "F2 - Choose the active model with the arrow-key picker.\n" +
            "F3 - Pin or hide the latest plan in the terminal view.\n" +
            "/ - Open command suggestions in the terminal input.\n" +
            "ArrowUp / ArrowDown on an empty composer - Switch the active subagent thread in supported UIs.\n\n" +
            "Multiline input: press Shift+Enter to insert a new line, then Enter to send.\n\n" +
            "Start with --session <session-guid> to resume a saved session. --section also works as a compatibility alias.\n" +
            "Start with --profile <name> to choose the initial session profile. Use --thinking <on|off> to choose initial thinking mode, or use /profile <name> and /thinking <on|off> inside an active session. Use -v or --version at startup to print the CLI version.\n" +
            "Invoke subagents for one turn with @<subagent-name>; primary agents can also delegate focused work with agent_delegate or coordinate several tasks with agent_orchestrate.";

        return Task.FromResult(ReplCommandResult.Continue(
            $"Active agent profile: {context.Session.AgentProfile.Name}\n\n{helpText}"));
    }
}
