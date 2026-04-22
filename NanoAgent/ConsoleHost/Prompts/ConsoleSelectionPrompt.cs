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

    public ConsoleSelectionPrompt(
        IConsoleInteractionGate interactionGate,
        IConsoleTerminal terminal,
        IConsolePromptRenderer renderer)
    {
        _interactionGate = interactionGate;
        _terminal = terminal;
        _renderer = renderer;
    }

    public Task<T> PromptAsync<T>(SelectionPromptRequest<T> request, CancellationToken cancellationToken)
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

        if (request.AutoSelectAfter < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Auto-select timeout cannot be negative.");
        }

        if (!SupportsInteractiveSelection())
        {
            return Task.FromException<T>(
                new InvalidOperationException("Selection prompts require an interactive terminal."));
        }

        return Task.FromResult(PromptInteractive(request, cancellationToken));
    }

    private T PromptInteractive<T>(SelectionPromptRequest<T> request, CancellationToken cancellationToken)
    {
        using IDisposable _ = _interactionGate.EnterScope();

        int selectedIndex = request.DefaultIndex;
        DateTimeOffset? autoSelectAt = request.AutoSelectAfter is { } autoSelectAfter
            ? DateTimeOffset.UtcNow.Add(autoSelectAfter)
            : null;
        int? remainingAutoSelectSeconds = autoSelectAt is null
            ? null
            : GetRemainingAutoSelectSeconds(autoSelectAt.Value);
        InteractiveSelectionPromptLayout layout = _renderer.WriteInteractiveSelectionPrompt(
            request,
            selectedIndex,
            remainingAutoSelectSeconds);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryAutoSelectDefault(
                    request,
                    autoSelectAt,
                    layout,
                    ref remainingAutoSelectSeconds,
                    out T autoSelectedValue))
                {
                    return autoSelectedValue;
                }

                if (autoSelectAt is not null && !_terminal.KeyAvailable)
                {
                    Thread.Sleep(GetInputPollDelay(autoSelectAt.Value));
                    continue;
                }

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

    private bool TryAutoSelectDefault<T>(
        SelectionPromptRequest<T> request,
        DateTimeOffset? autoSelectAt,
        InteractiveSelectionPromptLayout layout,
        ref int? remainingAutoSelectSeconds,
        out T selectedValue)
    {
        selectedValue = default!;
        if (autoSelectAt is null)
        {
            return false;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now >= autoSelectAt.Value)
        {
            selectedValue = request.Options[request.DefaultIndex].Value;
            return true;
        }

        int currentRemainingSeconds = GetRemainingAutoSelectSeconds(autoSelectAt.Value, now);
        if (remainingAutoSelectSeconds != currentRemainingSeconds)
        {
            remainingAutoSelectSeconds = currentRemainingSeconds;
            _renderer.RewriteSelectionDefaultLine(request, layout, currentRemainingSeconds);
        }

        return false;
    }

    private static int GetRemainingAutoSelectSeconds(DateTimeOffset autoSelectAt)
    {
        return GetRemainingAutoSelectSeconds(autoSelectAt, DateTimeOffset.UtcNow);
    }

    private static int GetRemainingAutoSelectSeconds(
        DateTimeOffset autoSelectAt,
        DateTimeOffset now)
    {
        TimeSpan remaining = autoSelectAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
    }

    private static TimeSpan GetInputPollDelay(DateTimeOffset autoSelectAt)
    {
        TimeSpan remaining = autoSelectAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return remaining < TimeSpan.FromMilliseconds(100)
            ? remaining
            : TimeSpan.FromMilliseconds(100);
    }

    private bool SupportsInteractiveSelection()
    {
        return !_terminal.IsInputRedirected && !_terminal.IsOutputRedirected;
    }
}
