using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IApplicationUpdateService
{
    Task<ApplicationUpdateInfo> CheckAsync(CancellationToken cancellationToken);

    Task<ApplicationUpdateInstallResult> InstallAsync(
        ApplicationUpdateInfo updateInfo,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
