using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class AutoCommitCommandHandler : IReplCommandHandler
{
    private readonly IWorkspaceSettingsWriter _workspaceSettingsWriter;

    public AutoCommitCommandHandler(IWorkspaceSettingsWriter workspaceSettingsWriter)
    {
        _workspaceSettingsWriter = workspaceSettingsWriter;
    }

    public string CommandName => "autocommit";

    public string Description => "Show or toggle automatic git commits for AI-made workspace changes.";

    public string Usage => "/autocommit [on|off|status]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string action = string.IsNullOrWhiteSpace(context.ArgumentText)
            ? "status"
            : context.ArgumentText.Trim().ToLowerInvariant();

        return action switch
        {
            "on" => await SaveAsync(context, enabled: true, cancellationToken),
            "off" => await SaveAsync(context, enabled: false, cancellationToken),
            "status" => ReplCommandResult.Continue(
                FormatStatus(context.Session.WorkspacePath),
                ReplFeedbackKind.Info),
            _ => ReplCommandResult.Continue(
                "Usage: /autocommit [on|off|status]",
                ReplFeedbackKind.Error)
        };
    }

    private async Task<ReplCommandResult> SaveAsync(
        ReplCommandContext context,
        bool enabled,
        CancellationToken cancellationToken)
    {
        await _workspaceSettingsWriter.SaveAutoCommitEnabledAsync(
            context.Session.WorkspacePath,
            enabled,
            cancellationToken);

        string state = enabled ? "enabled" : "disabled";
        return ReplCommandResult.Continue(
            $"Automatic AI commits {state} for this workspace in .nanoagent/agent-profile.json.",
            ReplFeedbackKind.Info);
    }

    private static string FormatStatus(string workspacePath)
    {
        string filePath = Path.Combine(
            Path.GetFullPath(workspacePath),
            ".nanoagent",
            "agent-profile.json");

        if (!File.Exists(filePath))
        {
            return "Automatic AI commits are enabled by default for this workspace.";
        }

        try
        {
            string json = File.ReadAllText(filePath);
            if (json.IndexOf("\"AutoCommitAfterAiChanges\": false", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Automatic AI commits are disabled for this workspace.";
            }

            if (json.IndexOf("\"AutoCommitAfterAiChanges\": true", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Automatic AI commits are enabled for this workspace.";
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return "Automatic AI commits are enabled by default for this workspace.";
    }
}
