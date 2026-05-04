using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

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
            "/budget [status|local|cloud] - Show or configure budget controls.\n" +
            "/clone - Duplicate the current session at the current position.\n" +
            "/compact [retained-turns] - Manually compact the session context.\n" +
            "/config - Show the current provider, session, config path, active profile, thinking, and active model.\n" +
            "/copy - Copy the last agent message to the clipboard.\n" +
            "/deny <tool-or-tag> [pattern] - Add a session-scoped deny override.\n" +
            "/exit - Exit the interactive shell.\n" +
            "/export [json|html] [path] - Export the current session.\n" +
            "/fork [turn-number] - Create a new fork from a previous user message.\n" +
            "/help - List the available shell commands and their usage.\n" +
            "/import <json-path> - Import a session from JSON.\n" +
            "/init [recommended|minimal|custom] - Choose and initialize workspace-local NanoAgent files.\n" +
            "/mcp - Show configured MCP servers, custom tool providers, and discovered dynamic tools.\n" +
            "/models - Choose the active model with the picker.\n" +
            "/new - Start a new session.\n" +
            "/onboard - Re-run provider onboarding and switch the active session to the new provider.\n" +
            "/permissions - Show the current permission summary and override guidance.\n" +
            "/profile <name> - Switch the active agent profile for subsequent prompts.\n" +
            "/reload - Reload keybindings, extensions, skills, prompts, and themes.\n" +
            "/redo - Re-apply the most recently undone file edit transaction.\n" +
            "/resume [session-id] - Resume a different session.\n" +
            "/rules - List the effective permission rules in evaluation order.\n" +
            "/session - Show session info and stats.\n" +
            "/setting [model|profile|thinking|provider|budget|workspace|permissions|tools|summary] - Open the settings picker or jump to a settings area.\n" +
            "/share - Share the current session as a secret GitHub gist.\n" +
            "/thinking [on|off] - Show or set simple thinking mode.\n" +
            "/tree - Navigate saved sessions and switch branches.\n" +
            "/update [now] - Check for updates. Use /update now to install without an extra prompt.\n" +
            "/undo - Roll back the most recent tracked file edit transaction.\n" +
            "/use <model> - Switch the active model directly.\n" +
            "!<shell-command> - Run a local shell command directly from the input box.\n" +
            "\nKeyboard shortcuts:\n" +
            "F2 - Choose the active model with the arrow-key picker.\n" +
            "F3 - Pin or hide the latest plan in the terminal view.\n" +
            "/ - Open command suggestions in the terminal input.\n\n" +
            "Multiline input: press Shift+Enter to insert a new line, then Enter to send.\n\n" +
            "Start with --section <section-guid> to resume a saved section.\n" +
            "Start with --profile <name> to choose the initial session profile. Use --thinking <on|off> to choose initial thinking mode, or use /profile <name> and /thinking <on|off> inside an active session.\n" +
            "Invoke subagents for one turn with @<subagent-name>; primary agents can also delegate focused work with agent_delegate or coordinate several tasks with agent_orchestrate.";

        return Task.FromResult(ReplCommandResult.Continue(
            $"Active agent profile: {context.Session.AgentProfile.Name}\n\n{HelpText}"));
    }
}
