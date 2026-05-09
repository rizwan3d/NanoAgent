using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using System.Globalization;

namespace NanoAgent.Application.Commands;

internal sealed class TerminalsCommandHandler : IReplCommandHandler
{
    private readonly IShellCommandService _shellCommandService;

    public TerminalsCommandHandler(IShellCommandService shellCommandService)
    {
        _shellCommandService = shellCommandService;
    }

    public string CommandName => "terminals";

    public string Description => "List or stop background terminals for the current session.";

    public string Usage => "/terminals [stop <terminal-id>|stop all]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Arguments.Count == 0)
        {
            return ReplCommandResult.Continue(await FormatListAsync(context, cancellationToken));
        }

        if (context.Arguments.Count >= 2 &&
            string.Equals(context.Arguments[0], "stop", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(context.Arguments[1], "all", StringComparison.OrdinalIgnoreCase)
                ? await StopAllAsync(context, cancellationToken)
                : await StopOneAsync(context, context.Arguments[1], cancellationToken);
        }

        return ReplCommandResult.Continue(
            $"Usage: {Usage}",
            ReplFeedbackKind.Error);
    }

    private async Task<string> FormatListAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BackgroundTerminalInfo> terminals = await ListSessionTerminalsAsync(
            context,
            cancellationToken);

        if (terminals.Count == 0)
        {
            return "No background terminals are active for this session.";
        }

        List<string> lines =
        [
            "Background terminals:",
            "Use /terminals stop <terminal-id> to stop one, or /terminals stop all to stop every running terminal in this session."
        ];
        lines.AddRange(terminals.Select(FormatTerminal));
        return string.Join(Environment.NewLine, lines);
    }

    private async Task<ReplCommandResult> StopOneAsync(
        ReplCommandContext context,
        string terminalId,
        CancellationToken cancellationToken)
    {
        string normalizedTerminalId = terminalId.Trim();
        IReadOnlyList<BackgroundTerminalInfo> terminals = await ListSessionTerminalsAsync(
            context,
            cancellationToken);

        if (!terminals.Any(terminal => string.Equals(terminal.Id, normalizedTerminalId, StringComparison.OrdinalIgnoreCase)))
        {
            return ReplCommandResult.Continue(
                $"Background terminal '{normalizedTerminalId}' was not found for this session.",
                ReplFeedbackKind.Error);
        }

        ShellCommandExecutionResult result = await _shellCommandService.StopBackgroundAsync(
            normalizedTerminalId,
            cancellationToken);

        return string.Equals(result.TerminalStatus, "not_found", StringComparison.Ordinal)
            ? ReplCommandResult.Continue(result.StandardError, ReplFeedbackKind.Error)
            : ReplCommandResult.Continue($"Stopped background terminal '{normalizedTerminalId}'.");
    }

    private async Task<ReplCommandResult> StopAllAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BackgroundTerminalInfo> terminals = await ListSessionTerminalsAsync(
            context,
            cancellationToken);
        BackgroundTerminalInfo[] runningTerminals = terminals
            .Where(static terminal => string.Equals(terminal.Status, "running", StringComparison.Ordinal))
            .ToArray();

        foreach (BackgroundTerminalInfo terminal in runningTerminals)
        {
            await _shellCommandService.StopBackgroundAsync(
                terminal.Id,
                cancellationToken);
        }

        return runningTerminals.Length == 0
            ? ReplCommandResult.Continue("No running background terminals are active for this session.")
            : ReplCommandResult.Continue(
                $"Stopped {runningTerminals.Length.ToString(CultureInfo.InvariantCulture)} background terminal(s).");
    }

    private Task<IReadOnlyList<BackgroundTerminalInfo>> ListSessionTerminalsAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        return _shellCommandService.ListBackgroundAsync(
            context.Session.SessionId,
            cancellationToken);
    }

    private static string FormatTerminal(BackgroundTerminalInfo terminal)
    {
        string exitCode = terminal.ExitCode is null
            ? "pending"
            : terminal.ExitCode.Value.ToString(CultureInfo.InvariantCulture);
        string expires = terminal.ExpiresAtUtc is null
            ? string.Empty
            : $", expires {terminal.ExpiresAtUtc.Value:u}";
        return $"- {terminal.Id}: {terminal.Status}, exit {exitCode}{expires}, cwd {terminal.WorkingDirectory}, command `{SuspiciousUnicodeText.RenderVisible(terminal.Command)}`";
    }
}
