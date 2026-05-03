using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IBudgetControlsUsageService
{
    Task ConfigureLocalAsync(
        ReplSessionContext session,
        string? localPath,
        BudgetControlsLocalOptions options,
        CancellationToken cancellationToken);

    Task<BudgetControlsStatus> GetStatusAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);

    Task RecordUsageAsync(
        ReplSessionContext session,
        BudgetControlsUsageDelta usage,
        CancellationToken cancellationToken);
}
