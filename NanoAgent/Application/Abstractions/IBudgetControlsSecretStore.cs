namespace NanoAgent.Application.Abstractions;

public interface IBudgetControlsSecretStore
{
    Task<string?> LoadCloudAuthKeyAsync(CancellationToken cancellationToken);

    Task SaveCloudAuthKeyAsync(
        string authKey,
        CancellationToken cancellationToken);
}
