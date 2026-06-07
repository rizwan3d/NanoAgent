using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Commands;

internal sealed class RedactCommandHandler : IReplCommandHandler
{
    public string CommandName => "redact";

    public string Description => "Show or toggle secret redaction for session output.";

    public string Usage => "/redact [on|off]";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                $"Secret redaction: {(SecretRedactor.IsEnabled ? "on" : "off")}. " +
                "Use /redact on or /redact off."));
        }

        string requestedMode = context.ArgumentText.Trim().ToLowerInvariant();

        return requestedMode switch
        {
            "on" or "enable" or "true" or "1" => ToggleRedact(true),
            "off" or "disable" or "false" or "0" => ToggleRedact(false),
            _ => Task.FromResult(ReplCommandResult.Continue(
                $"Unsupported value '{requestedMode}'. Use /redact on or /redact off.",
                ReplFeedbackKind.Error))
        };
    }

    private static Task<ReplCommandResult> ToggleRedact(bool enabled)
    {
        if (SecretRedactor.IsEnabled == enabled)
        {
            return Task.FromResult(ReplCommandResult.Continue(
                $"Secret redaction is already {(enabled ? "on" : "off")}."));
        }

        SecretRedactor.IsEnabled = enabled;
        return Task.FromResult(ReplCommandResult.Continue(
            $"Secret redaction turned {(enabled ? "on" : "off")}."));
    }
}
