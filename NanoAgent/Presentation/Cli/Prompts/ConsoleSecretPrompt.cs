using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Presentation.Cli.Terminal;

namespace NanoAgent.Presentation.Cli.Prompts;

internal sealed class ConsoleSecretPrompt : ISecretPrompt
{
    private readonly IConsoleTerminal _terminal;
    private readonly IConsolePromptInputReader _inputReader;
    private readonly IConsolePromptRenderer _renderer;

    public ConsoleSecretPrompt(
        IConsoleTerminal terminal,
        IConsolePromptInputReader inputReader,
        IConsolePromptRenderer renderer)
    {
        _terminal = terminal;
        _inputReader = inputReader;
        _renderer = renderer;
    }

    public async Task<string> PromptAsync(SecretPromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        _renderer.WriteSecretPrompt(request);

        if (SupportsInteractiveInput())
        {
            return await _inputReader.ReadLineAsync(
                defaultValue: null,
                echoMode: ConsoleInputEchoMode.SecretMask,
                request.AllowCancellation,
                cancellationToken);
        }

        string? value = _terminal.ReadLine();
        if (value is null)
        {
            throw new PromptCancelledException("The input stream closed before a secret value was provided.");
        }

        return value;
    }

    private bool SupportsInteractiveInput()
    {
        return !_terminal.IsInputRedirected && !_terminal.IsOutputRedirected;
    }
}
