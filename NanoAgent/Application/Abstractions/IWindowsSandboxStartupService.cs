namespace NanoAgent.Application.Abstractions;

public interface IWindowsSandboxStartupService
{
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    Task<WindowsSandboxSetupResult> SetupAsync(CancellationToken cancellationToken);
}
