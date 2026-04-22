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
            "/thinking [effort|default] - Show or set thinking effort: none, minimal, low, medium, high, or xhigh.\n" +
            "/undo - Roll back the most recent tracked file edit transaction.\n" +
            "/use <model> - Switch the active model for subsequent prompts.\n\n" +
            "Start with --profile build, --profile plan, or --profile review to choose the initial session profile. Use --thinking <effort> to choose initial thinking effort, or use /profile <name> and /thinking <effort> inside an active session.";

        return Task.FromResult(ReplCommandResult.Continue(
            $"Active agent profile: {context.Session.AgentProfile.Name}\n\n{HelpText}"));
    }
}
