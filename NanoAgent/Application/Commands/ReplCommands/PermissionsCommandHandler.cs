using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class PermissionsCommandHandler : IReplCommandHandler
{
    private readonly PermissionSettings _permissionSettings;

    public PermissionsCommandHandler(PermissionSettings permissionSettings)
    {
        _permissionSettings = permissionSettings;
    }

    public string CommandName => "permissions";

    public string Description => "Show the current permission summary and session override guidance.";

    public string Usage => "/permissions";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                "Usage: /permissions",
                ReplFeedbackKind.Error));
        }

        return Task.FromResult(ReplCommandResult.Continue(
            PermissionCommandSupport.BuildPermissionsSummary(_permissionSettings, context.Session),
            ReplFeedbackKind.Info));
    }
}
