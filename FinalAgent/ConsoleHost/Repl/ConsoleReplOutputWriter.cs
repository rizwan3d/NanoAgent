using FinalAgent.Application.Abstractions;
using FinalAgent.ConsoleHost.Terminal;

namespace FinalAgent.ConsoleHost.Repl;

internal sealed class ConsoleReplOutputWriter : IReplOutputWriter
{
    private readonly IConsolePromptRenderer _renderer;
    private readonly IConsoleTerminal _terminal;

    public ConsoleReplOutputWriter(
        IConsolePromptRenderer renderer,
        IConsoleTerminal terminal)
    {
        _renderer = renderer;
        _terminal = terminal;
    }

    public Task WriteErrorAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _renderer.WriteStatus(StatusMessageKind.Error, message);
        return Task.CompletedTask;
    }

    public Task WriteInfoAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _renderer.WriteStatus(StatusMessageKind.Info, message);
        return Task.CompletedTask;
    }

    public Task WriteResponseAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (string line in NormalizeLines(message))
        {
            _terminal.WriteLine($"assistant> {line}");
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<string> NormalizeLines(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return [" "];
        }

        return message
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
    }
}
