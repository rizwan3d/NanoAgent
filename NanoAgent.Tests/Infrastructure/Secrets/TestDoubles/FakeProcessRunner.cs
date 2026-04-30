using NanoAgent.Infrastructure.Secrets;

namespace NanoAgent.Tests.Infrastructure.Secrets.TestDoubles;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<IBackgroundProcess> _backgroundProcesses = new();
    private readonly Queue<ProcessExecutionResult> _results = new();

    public List<ProcessExecutionRequest> BackgroundRequests { get; } = [];

    public List<ProcessExecutionRequest> Requests { get; } = [];

    public void EnqueueBackgroundProcess(IBackgroundProcess process)
    {
        _backgroundProcesses.Enqueue(process);
    }

    public void EnqueueResult(ProcessExecutionResult result)
    {
        _results.Enqueue(result);
    }

    public Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);

        if (_results.Count == 0)
        {
            throw new InvalidOperationException("No queued process result is available.");
        }

        return Task.FromResult(_results.Dequeue());
    }

    public IBackgroundProcess StartBackground(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BackgroundRequests.Add(request);

        if (_backgroundProcesses.Count == 0)
        {
            throw new InvalidOperationException("No queued background process is available.");
        }

        return _backgroundProcesses.Dequeue();
    }
}

internal sealed class FakeBackgroundProcess : IBackgroundProcess
{
    private bool _hasExited;

    public FakeBackgroundProcess(
        string standardOutput = "",
        string standardError = "",
        int exitCode = 0)
    {
        StandardOutput = new StringReader(standardOutput);
        StandardError = new StringReader(standardError);
        ExitCode = exitCode;
    }

    public TextReader StandardOutput { get; }

    public TextReader StandardError { get; }

    public bool HasExited => _hasExited;

    public int ExitCode { get; }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _hasExited = true;
        return Task.CompletedTask;
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _hasExited = true;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StandardOutput.Dispose();
        StandardError.Dispose();
    }
}
