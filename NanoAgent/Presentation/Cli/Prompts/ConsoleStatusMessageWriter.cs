using NanoAgent.Application.Abstractions;
using NanoAgent.Presentation.Cli.Terminal;

namespace NanoAgent.Presentation.Cli.Prompts;

internal sealed class ConsoleStatusMessageWriter : IStatusMessageWriter
{
    private readonly IConsolePromptRenderer _renderer;

    public ConsoleStatusMessageWriter(IConsolePromptRenderer renderer)
    {
        _renderer = renderer;
    }

    public Task ShowErrorAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _renderer.WriteStatus(StatusMessageKind.Error, message);
        return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task ShowSuccessAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _renderer.WriteStatus(StatusMessageKind.Success, message);
        return Task.CompletedTask;
    }
}
