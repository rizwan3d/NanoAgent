using StemCode.Application.Models;
using StemCode.Application.Tools.Models;

namespace StemCode.Application.Abstractions;

public interface ISkillService
{
    Task<IReadOnlyList<WorkspaceSkillDescriptor>> ListAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);

    Task<string?> CreateRoutingPromptAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);

    Task<WorkspaceSkillLoadResult?> LoadAsync(
        ReplSessionContext session,
        string name,
        CancellationToken cancellationToken);
}
