using StemCode.Infrastructure.Secrets;

namespace StemCode.Infrastructure.WindowsSandbox;

internal interface IWindowsSandboxProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken);

    Task<IBackgroundProcessHandle> StartBackgroundAsync(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken);
}
