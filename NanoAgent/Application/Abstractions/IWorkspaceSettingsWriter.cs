using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IWorkspaceSettingsWriter
{
    Task SavePermissionSettingsAsync(
        string workspacePath,
        PermissionSettings settings,
        CancellationToken cancellationToken);

    Task SaveTelemetryEnabledAsync(
        string workspacePath,
        bool enabled,
        CancellationToken cancellationToken);
}
