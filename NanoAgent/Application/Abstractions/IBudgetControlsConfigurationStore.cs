using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IBudgetControlsConfigurationStore
{
    Task<BudgetControlsSettings?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        BudgetControlsSettings settings,
        CancellationToken cancellationToken);
}
