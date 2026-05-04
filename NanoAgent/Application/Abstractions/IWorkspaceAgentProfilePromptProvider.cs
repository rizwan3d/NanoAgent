using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IWorkspaceAgentProfilePromptProvider
{
    Task<string?> LoadAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
