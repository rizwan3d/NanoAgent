using NanoAgent.Infrastructure.Secrets;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal interface IWindowsSandboxProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken);
}
