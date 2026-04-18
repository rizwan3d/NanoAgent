using FinalAgent.Infrastructure.Secrets;

namespace FinalAgent.Tests.Infrastructure.Secrets.TestDoubles;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessExecutionResult> _results = new();

    public List<ProcessExecutionRequest> Requests { get; } = [];

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
}
