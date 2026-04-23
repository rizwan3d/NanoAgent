using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Presentation.Abstractions;

namespace NanoAgent.Presentation.Repl.Commands;

internal sealed class PermissionsCommandHandler : IReplCommandHandler
{
    private readonly IPermissionConfigurationAccessor _permissionConfigurationAccessor;

    public PermissionsCommandHandler(IPermissionConfigurationAccessor permissionConfigurationAccessor)
    {
        _permissionConfigurationAccessor = permissionConfigurationAccessor;
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

        PermissionSettings settings = _permissionConfigurationAccessor.GetSettings();
        return Task.FromResult(ReplCommandResult.Continue(
            PermissionCommandSupport.BuildPermissionsSummary(settings, context.Session),
            ReplFeedbackKind.Info));
    }
}
