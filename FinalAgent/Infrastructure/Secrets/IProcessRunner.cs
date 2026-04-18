namespace FinalAgent.Infrastructure.Secrets;

internal interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken);
}
