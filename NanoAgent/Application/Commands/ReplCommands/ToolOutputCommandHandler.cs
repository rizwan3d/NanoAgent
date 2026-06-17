using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class ToolOutputCommandHandler : IReplCommandHandler
{
    public string CommandName => "tooloutput";

    public string Description => "Show or toggle whether tool results print their complete output or a compact preview.";

    public string Usage => "/tooloutput [compact|full|auto]";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return Task.FromResult(ReplCommandResult.Continue(DescribeState(context)));
        }

        string requestedMode = context.ArgumentText.Trim().ToLowerInvariant();

        return requestedMode switch
        {
            "full" or "complete" or "on" or "all" => SetOverride(context, true),
            "compact" or "preview" or "off" or "short" => SetOverride(context, false),
            "auto" or "profile" or "default" or "clear" => ClearOverride(context),
            _ => Task.FromResult(ReplCommandResult.Continue(
                $"Unsupported value '{requestedMode}'. Use /tooloutput compact, /tooloutput full, or /tooloutput auto.",
                ReplFeedbackKind.Error))
        };
    }

    private static Task<ReplCommandResult> SetOverride(ReplCommandContext context, bool showFull)
    {
        if (ToolOutputDisplay.FullToolOutputOverride == showFull)
        {
            return Task.FromResult(ReplCommandResult.Continue(
                $"Tool output is already set to {DescribeMode(showFull)}."));
        }

        ToolOutputDisplay.FullToolOutputOverride = showFull;
        return Task.FromResult(ReplCommandResult.Continue(
            $"Tool output set to {DescribeMode(showFull)}."));
    }

    private static Task<ReplCommandResult> ClearOverride(ReplCommandContext context)
    {
        ToolOutputDisplay.FullToolOutputOverride = null;
        return Task.FromResult(ReplCommandResult.Continue(
            $"Tool output follows the active agent profile ('{context.Session.AgentProfile.Name}'): " +
            $"{DescribeMode(ToolOutputDisplay.ShowFullToolOutput)}."));
    }

    private static string DescribeState(ReplCommandContext context)
    {
        string source = ToolOutputDisplay.FullToolOutputOverride is not null
            ? "command override"
            : ToolOutputDisplay.ProfileFullToolOutput is not null
                ? $"profile '{context.Session.AgentProfile.Name}'"
                : "default";

        return $"Tool output: {DescribeMode(ToolOutputDisplay.ShowFullToolOutput)} (from {source}). " +
            "Use /tooloutput compact, /tooloutput full, or /tooloutput auto.";
    }

    private static string DescribeMode(bool showFull)
    {
        return showFull ? "full (complete output)" : "compact (preview)";
    }
}
