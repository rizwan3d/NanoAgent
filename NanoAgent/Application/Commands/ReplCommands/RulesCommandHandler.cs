using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class RulesCommandHandler : IReplCommandHandler
{
    private readonly PermissionSettings _permissionSettings;

    public RulesCommandHandler(PermissionSettings permissionSettings)
    {
        _permissionSettings = permissionSettings;
    }

    public string CommandName => "rules";

    public string Description => "List the effective permission rules in evaluation order.";

    public string Usage => "/rules";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                "Usage: /rules",
                ReplFeedbackKind.Error));
        }

        return Task.FromResult(ReplCommandResult.Continue(
            PermissionCommandSupport.BuildRulesListing(_permissionSettings, context.Session),
            ReplFeedbackKind.Info));
    }
}
