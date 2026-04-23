using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Repl.Commands;

internal sealed class HelpCommandHandler : IReplCommandHandler
{
    public string CommandName => "help";

    public string Description => "List the available shell commands and their usage.";

    public string Usage => "/help";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        const string HelpText =
            "Available commands:\n" +
            "/allow <tool-or-tag> [pattern] - Add a session-scoped allow override.\n" +
            "/config - Show the current provider, session, config path, active profile, thinking, and active model.\n" +
            "/deny <tool-or-tag> [pattern] - Add a session-scoped deny override.\n" +
            "/exit - Exit the interactive shell.\n" +
            "/help - List the available shell commands and their usage.\n" +
            "/models - Show the available models in the current session.\n" +
            "/permissions - Show the current permission summary and override guidance.\n" +
            "/profile <name> - Switch the active agent profile for subsequent prompts.\n" +
            "/redo - Re-apply the most recently undone file edit transaction.\n" +
            "/rules - List the effective permission rules in evaluation order.\n" +
            "/thinking [on|off] - Show or set simple thinking mode.\n" +
            "/undo - Roll back the most recent tracked file edit transaction.\n" +
            "/use <model> - Switch the active model for subsequent prompts.\n\n" +
            "Multiline input: enter \"\"\" on its own line, then finish with \"\"\" on its own line.\n\n" +
            "Start with --profile build, --profile plan, or --profile review to choose the initial session profile. Use --thinking <on|off> to choose initial thinking mode, or use /profile <name> and /thinking <on|off> inside an active session.\n" +
            "Invoke subagents for one turn with @general or @explore; primary agents can also delegate focused work with agent_delegate.";

        return Task.FromResult(ReplCommandResult.Continue(
            $"Active agent profile: {context.Session.AgentProfile.Name}\n\n{HelpText}"));
    }
}
