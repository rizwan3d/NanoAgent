using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IApplicationUpdateService
{
    Task<ApplicationUpdateInfo> CheckAsync(CancellationToken cancellationToken);

    Task<ApplicationUpdateInstallResult> InstallAsync(
        ApplicationUpdateInfo updateInfo,
        CancellationToken cancellationToken);
}
