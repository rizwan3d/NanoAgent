namespace NanoAgent.Infrastructure.Secrets;

internal interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken);

    IBackgroundProcess StartBackground(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Background process execution is not supported.");
    }
}
