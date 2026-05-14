using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class DisableAnalyticsCommandHandler : IReplCommandHandler
{
    private readonly IWorkspaceSettingsWriter _workspaceSettingsWriter;

    public DisableAnalyticsCommandHandler(IWorkspaceSettingsWriter workspaceSettingsWriter)
    {
        _workspaceSettingsWriter = workspaceSettingsWriter;
    }

    public string CommandName => "disableanalytics";

    public string Description => "Disable product analytics for this workspace.";

    public string Usage => "/disableanalytics";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return ReplCommandResult.Continue(
                "Usage: /disableanalytics",
                ReplFeedbackKind.Error);
        }

        await _workspaceSettingsWriter.SaveTelemetryEnabledAsync(
            context.Session.WorkspacePath,
            enabled: false,
            cancellationToken);

        return ReplCommandResult.Continue(
            "Analytics disabled for this workspace in .nanoagent/agent-profile.json. Restart NanoAgent to apply the change.",
            ReplFeedbackKind.Info);
    }
}
