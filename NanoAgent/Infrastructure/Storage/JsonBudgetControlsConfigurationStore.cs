using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Storage;

internal sealed class JsonBudgetControlsConfigurationStore : IBudgetControlsConfigurationStore
{
    private readonly IUserDataPathProvider _pathProvider;

    public JsonBudgetControlsConfigurationStore(IUserDataPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public async Task<BudgetControlsSettings?> LoadAsync(CancellationToken cancellationToken)
    {
        AgentProfileConfigurationDocument? document =
            await AgentProfileConfigurationReader.LoadUserDocumentAsync(
                _pathProvider,
                cancellationToken);

        return document?.BudgetControls is null
            ? null
            : BudgetControlsSettings.NormalizeOrDefault(document.BudgetControls);
    }

    public async Task SaveAsync(
        BudgetControlsSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AgentProfileConfigurationDocument document =
            await AgentProfileConfigurationReader.LoadUserDocumentAsync(
                _pathProvider,
                cancellationToken) ??
            new AgentProfileConfigurationDocument();

        document.BudgetControls = BudgetControlsSettings.NormalizeOrDefault(settings);

        await AgentProfileConfigurationReader.SaveUserDocumentAsync(
            _pathProvider,
            document,
            cancellationToken);
    }
}
