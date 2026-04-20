using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.ConsoleHost.Terminal;

namespace NanoAgent.ConsoleHost.Prompts;

internal sealed class ConsoleSelectionPrompt : ISelectionPrompt
{
    private readonly IConsoleInteractionGate _interactionGate;
    private readonly IConsoleTerminal _terminal;
    private readonly IConsolePromptRenderer _renderer;
    private readonly IStatusMessageWriter _statusMessageWriter;

    public ConsoleSelectionPrompt(
        IConsoleInteractionGate interactionGate,
        IConsoleTerminal terminal,
        IConsolePromptRenderer renderer,
        IStatusMessageWriter statusMessageWriter)
    {
        _interactionGate = interactionGate;
        _terminal = terminal;
        _renderer = renderer;
        _statusMessageWriter = statusMessageWriter;
    }

    public async Task<T> PromptAsync<T>(SelectionPromptRequest<T> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Options.Count == 0)
        {
            throw new ArgumentException("At least one option must be provided.", nameof(request));
        }

        if (request.DefaultIndex < 0 || request.DefaultIndex >= request.Options.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Default index must reference a valid option.");
        }

        return SupportsInteractiveSelection()
            ? PromptInteractiveAsync(request, cancellationToken)
            : await PromptFallbackAsync(request, cancellationToken);
    }

    private T PromptInteractiveAsync<T>(SelectionPromptRequest<T> request, CancellationToken cancellationToken)
    {
        using IDisposable _ = _interactionGate.EnterScope();

        int selectedIndex = request.DefaultIndex;
        InteractiveSelectionPromptLayout layout = _renderer.WriteInteractiveSelectionPrompt(request, selectedIndex);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ConsoleKeyInfo keyInfo = _terminal.ReadKey(intercept: true);
                switch (keyInfo.Key)
                {
                    case ConsoleKey.UpArrow:
                        if (selectedIndex > 0)
                        {
                            selectedIndex--;
                            _renderer.RewriteSelectionOptions(request, selectedIndex, layout);
                        }

                        break;

                    case ConsoleKey.DownArrow:
                        if (selectedIndex < request.Options.Count - 1)
                        {
                            selectedIndex++;
                            _renderer.RewriteSelectionOptions(request, selectedIndex, layout);
                        }

                        break;

                    case ConsoleKey.Home:
                        if (selectedIndex != 0)
                        {
                            selectedIndex = 0;
                            _renderer.RewriteSelectionOptions(request, selectedIndex, layout);
                        }

                        break;

                    case ConsoleKey.End:
                        if (selectedIndex != request.Options.Count - 1)
                        {
                            selectedIndex = request.Options.Count - 1;
                            _renderer.RewriteSelectionOptions(request, selectedIndex, layout);
                        }

                        break;

                    case ConsoleKey.Enter:
                        return request.Options[selectedIndex].Value;

                    case ConsoleKey.Escape when request.AllowCancellation:
                        throw new PromptCancelledException();
                }
            }
        }
        finally
        {
            _renderer.ClearInteractiveSelectionPrompt(layout);
        }
    }

    private async Task<T> PromptFallbackAsync<T>(SelectionPromptRequest<T> request, CancellationToken cancellationToken)
    {
        using IDisposable _ = _interactionGate.EnterScope();

        _renderer.WriteFallbackSelectionPrompt(request);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _terminal.Write($"Selection [{request.DefaultIndex + 1}]: ");

            string? input = _terminal.ReadLine();
            if (input is null)
            {
                throw new PromptCancelledException("The input stream closed before a selection was made.");
            }

            string normalized = input.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return request.Options[request.DefaultIndex].Value;
            }

            if (int.TryParse(normalized, out int selectedValue) &&
                selectedValue >= 1 &&
                selectedValue <= request.Options.Count)
            {
                return request.Options[selectedValue - 1].Value;
            }

            await _statusMessageWriter.ShowErrorAsync(
                $"Enter a number between 1 and {request.Options.Count}.",
                cancellationToken);
        }
    }

    private bool SupportsInteractiveSelection()
    {
        return !_terminal.IsInputRedirected && !_terminal.IsOutputRedirected;
    }
}
