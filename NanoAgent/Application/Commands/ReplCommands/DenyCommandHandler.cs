using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class DenyCommandHandler : IReplCommandHandler
{
    public string CommandName => "deny";

    public string Description => "Add a session-scoped deny override for a tool/tag and optional target pattern.";

    public string Usage => "/deny <tool-or-tag> [pattern]";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!PermissionCommandSupport.TryParseOverrideArguments(
                context,
                Usage,
                out string toolPattern,
                out string? subjectPattern,
                out ReplCommandResult? errorResult))
        {
            return Task.FromResult(errorResult!);
        }

        return Task.FromResult(PermissionCommandSupport.AddSessionOverride(
            context.Session,
            PermissionMode.Deny,
            toolPattern,
            subjectPattern));
    }
}
