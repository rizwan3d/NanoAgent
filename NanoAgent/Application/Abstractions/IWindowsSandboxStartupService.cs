namespace NanoAgent.Application.Abstractions;

public interface IWindowsSandboxStartupService
{
    Task EnsureReadyAsync(CancellationToken cancellationToken);
}
