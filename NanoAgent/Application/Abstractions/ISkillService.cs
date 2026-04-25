using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Abstractions;

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
