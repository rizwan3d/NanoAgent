using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IWorkspaceSystemPromptProvider
{
    Task<string?> LoadAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
