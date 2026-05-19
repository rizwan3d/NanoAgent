using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Telemetry;

namespace NanoAgent.Application.Commands;

internal sealed class VersionCommandHandler : IReplCommandHandler
{
    public string CommandName => "version";

    public string Description => "Show the current NanoAgent CLI version.";

    public string Usage => "/version";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            ReplCommandResult.Continue(
                $"NanoAgent CLI {ProductTelemetryHelpers.GetNanoAgentVersion()}"));
    }
}
