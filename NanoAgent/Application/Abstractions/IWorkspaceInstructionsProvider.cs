using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IWorkspaceInstructionsProvider
{
    Task<string?> LoadAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
