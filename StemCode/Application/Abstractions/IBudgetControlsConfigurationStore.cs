using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IBudgetControlsConfigurationStore
{
    Task<BudgetControlsSettings?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        BudgetControlsSettings settings,
        CancellationToken cancellationToken);
}
