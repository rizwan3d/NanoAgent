using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Presentation.Cli.Terminal;

namespace NanoAgent.Presentation.Cli.Prompts;

internal sealed class ConsoleTextPrompt : ITextPrompt
{
    private readonly IConsoleTerminal _terminal;
    private readonly IConsolePromptInputReader _inputReader;
    private readonly IConsolePromptRenderer _renderer;

    public ConsoleTextPrompt(
        IConsoleTerminal terminal,
        IConsolePromptInputReader inputReader,
        IConsolePromptRenderer renderer)
    {
        _terminal = terminal;
        _inputReader = inputReader;
        _renderer = renderer;
    }

    public async Task<string> PromptAsync(TextPromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        _renderer.WriteTextPrompt(request);

        if (SupportsInteractiveInput())
        {
            return await _inputReader.ReadLineAsync(
                request.DefaultValue,
                ConsoleInputEchoMode.PlainText,
                request.AllowCancellation,
                cancellationToken);
        }

        string? value = _terminal.ReadLine();
        if (value is null)
        {
            throw new PromptCancelledException("The input stream closed before a value was provided.");
        }

        return string.IsNullOrWhiteSpace(value) && request.DefaultValue is not null
            ? request.DefaultValue
            : value;
    }

    private bool SupportsInteractiveInput()
    {
        return !_terminal.IsInputRedirected && !_terminal.IsOutputRedirected;
    }
}
