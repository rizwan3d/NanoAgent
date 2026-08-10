using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IWorkspaceInstructionsProvider
{
    Task<string?> LoadAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
