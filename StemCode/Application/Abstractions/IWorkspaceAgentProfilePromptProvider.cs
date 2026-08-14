using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IWorkspaceAgentProfilePromptProvider
{
    Task<string?> LoadAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
