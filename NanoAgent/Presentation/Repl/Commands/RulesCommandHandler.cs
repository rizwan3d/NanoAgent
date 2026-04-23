using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Presentation.Abstractions;

namespace NanoAgent.Presentation.Repl.Commands;

internal sealed class RulesCommandHandler : IReplCommandHandler
{
    private readonly IPermissionConfigurationAccessor _permissionConfigurationAccessor;

    public RulesCommandHandler(IPermissionConfigurationAccessor permissionConfigurationAccessor)
    {
        _permissionConfigurationAccessor = permissionConfigurationAccessor;
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

        PermissionSettings settings = _permissionConfigurationAccessor.GetSettings();
        return Task.FromResult(ReplCommandResult.Continue(
            PermissionCommandSupport.BuildRulesListing(settings, context.Session),
            ReplFeedbackKind.Info));
    }
}
