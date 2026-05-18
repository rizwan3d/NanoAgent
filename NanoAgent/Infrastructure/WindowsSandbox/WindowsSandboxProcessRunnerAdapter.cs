using NanoAgent.Infrastructure.Secrets;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal sealed class WindowsSandboxProcessRunnerAdapter : IWindowsSandboxProcessRunner
{
    public Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        WindowsSandboxExecutionContext context,
        CancellationToken cancellationToken)
    {
        return WindowsSandboxProcessRunner.RunAsync(
            request,
            context,
            cancellationToken);
    }
}
