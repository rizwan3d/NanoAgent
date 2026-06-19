using NanoAgent.Infrastructure.Secrets;

namespace NanoAgent.Tests.Infrastructure.Secrets.TestDoubles;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessExecutionResult> _results = new();
    private readonly Queue<IReadOnlyList<string>> _outputLines = new();

    public List<ProcessExecutionRequest> Requests { get; } = [];

    public void EnqueueResult(ProcessExecutionResult result, params string[] outputLines)
    {
        _results.Enqueue(result);
        _outputLines.Enqueue(outputLines);
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

        IReadOnlyList<string> lines = _outputLines.Count > 0
            ? _outputLines.Dequeue()
            : [];

        if (request.OnOutputLine is not null)
        {
            foreach (string line in lines)
            {
                request.OnOutputLine(line);
            }
        }

        return Task.FromResult(_results.Dequeue());
    }
}
