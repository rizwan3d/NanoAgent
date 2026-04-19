using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.ConsoleHost.Rendering;
using NanoAgent.ConsoleHost.Terminal;

namespace NanoAgent.ConsoleHost.Repl;

internal sealed class ConsoleReplOutputWriter : IReplOutputWriter
{
    private const double EstimatedTokensPerSecond = 4d;
    private static readonly TimeSpan ProgressUpdateInterval = TimeSpan.FromMilliseconds(250);

    private const int HeaderDividerWidth = 53;
    private const string RepositoryUrl = "github.com/rizwan3d/NanoAgent";
    private const string SponsorName = "ALFAIN Technologies (PVT) Limited";
    private const string SponsorUrl = "https://alfain.co/";

    private readonly ICliMessageFormatter _formatter;
    private readonly ICliTextRenderer _renderer;
    private readonly ICliOutputTarget _outputTarget;
    private readonly IConsoleTerminal _terminal;

    public ConsoleReplOutputWriter(
        ICliMessageFormatter formatter,
        ICliTextRenderer renderer,
        ICliOutputTarget outputTarget,
        IConsoleTerminal terminal)
    {
        _formatter = formatter;
        _renderer = renderer;
        _outputTarget = outputTarget;
        _terminal = terminal;
    }

    public Task WriteErrorAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _renderer.RenderAsync(
            _formatter.Format(CliRenderMessageKind.Error, message),
            cancellationToken);
    }

    public ValueTask<IAsyncDisposable> BeginResponseProgressAsync(
        int estimatedOutputTokens,
        int completedSessionEstimatedOutputTokens,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_terminal.IsOutputRedirected)
        {
            return ValueTask.FromResult<IAsyncDisposable>(NoOpAsyncDisposable.Instance);
        }

        return ValueTask.FromResult<IAsyncDisposable>(
            new ProgressScope(
                _terminal,
                estimatedOutputTokens,
                completedSessionEstimatedOutputTokens));
    }

    public Task WriteShellHeaderAsync(
        string applicationName,
        string modelName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        _outputTarget.WriteLine();
        _outputTarget.WriteLine([
            new CliOutputSegment("  ", CliOutputStyle.Muted),
            new CliOutputSegment(applicationName.Trim(), CliOutputStyle.Warning)
        ]);
        _outputTarget.WriteLine([
            new CliOutputSegment("  Model: ", CliOutputStyle.Muted),
            new CliOutputSegment(modelName.Trim(), CliOutputStyle.InlineCode)
        ]);
        _outputTarget.WriteLine([
            new CliOutputSegment("  GitHub: ", CliOutputStyle.Muted),
            new CliOutputSegment(RepositoryUrl, CliOutputStyle.Info)
        ]);
        _outputTarget.WriteLine([
            new CliOutputSegment("  Sponsor: ", CliOutputStyle.Muted),
            new CliOutputSegment(SponsorName, CliOutputStyle.Warning),
            new CliOutputSegment(" ", CliOutputStyle.Muted),
            new CliOutputSegment($"({SponsorUrl})", CliOutputStyle.Emphasis)
        ]);
        _outputTarget.WriteLine([
            new CliOutputSegment("  ", CliOutputStyle.Muted),
            new CliOutputSegment(new string('\u2500', HeaderDividerWidth), CliOutputStyle.CodeFence)
        ]);
        _outputTarget.WriteLine([
            new CliOutputSegment(
                "  Chat in the terminal. Press Ctrl+C or use /exit to quit.",
                CliOutputStyle.Muted)
        ]);
        _outputTarget.WriteLine();

        return Task.CompletedTask;
    }

    public Task WriteInfoAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _renderer.RenderAsync(
            _formatter.Format(CliRenderMessageKind.Info, message),
            cancellationToken);
    }

    public Task WriteWarningAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _renderer.RenderAsync(
            _formatter.Format(CliRenderMessageKind.Warning, message),
            cancellationToken);
    }

    public Task WriteResponseAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return WriteResponseAsync(message, null, cancellationToken);
    }

    public async Task WriteResponseAsync(
        string message,
        ConversationTurnMetrics? metrics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _renderer.RenderAsync(
            _formatter.Format(CliRenderMessageKind.Assistant, message),
            cancellationToken);

        if (metrics is null)
        {
            return;
        }

        _outputTarget.WriteLine([
            new CliOutputSegment("  ", CliOutputStyle.Muted),
            new CliOutputSegment(metrics.ToDisplayText(), CliOutputStyle.Muted)
        ]);
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static NoOpAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProgressScope : IAsyncDisposable
    {
        private readonly IConsoleTerminal _terminal;
        private readonly int _sessionSeedEstimatedTokens;
        private readonly int _lineTop;
        private readonly CancellationTokenSource _cancellationSource = new();
        private readonly Task _updateTask;

        public ProgressScope(
            IConsoleTerminal terminal,
            int estimatedOutputTokens,
            int completedSessionEstimatedOutputTokens)
        {
            _terminal = terminal;
            _sessionSeedEstimatedTokens = Math.Max(0, completedSessionEstimatedOutputTokens) +
                Math.Max(1, estimatedOutputTokens);
            _lineTop = terminal.CursorTop;

            if (TryWriteStatusLine(TimeSpan.Zero))
            {
                TryWriteTrailingNewLine();
            }

            _updateTask = RunAsync(_cancellationSource.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _cancellationSource.Cancel();

            try
            {
                await _updateTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                ClearStatusLine();
                _terminal.SetCursorPosition(0, _lineTop);
                _cancellationSource.Dispose();
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            using PeriodicTimer timer = new(ProgressUpdateInterval);
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryWriteStatusLine(DateTimeOffset.UtcNow - startedAt))
                {
                    break;
                }
            }
        }

        private void ClearStatusLine()
        {
            TryWriteLine(string.Empty);
        }

        private bool TryWriteStatusLine(TimeSpan elapsed)
        {
            return TryWriteLine(
                $"  ({FormatElapsed(elapsed)} \u00B7 \u2193 {CalculateRealtimeEstimate(elapsed)} tokens est.)");
        }

        private bool TryWriteLine(string value)
        {
            int width = Math.Max(1, _terminal.WindowWidth - 1);
            string padded = value.Length > width
                ? value[..Math.Max(0, width - 3)] + "..."
                : value.PadRight(width);

            ConsoleColor originalForeground = _terminal.ForegroundColor;
            ConsoleColor originalBackground = _terminal.BackgroundColor;

            try
            {
                _terminal.SetCursorPosition(0, _lineTop);
                _terminal.ForegroundColor = ConsoleColor.DarkGray;
                _terminal.Write(padded);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            finally
            {
                _terminal.ForegroundColor = originalForeground;
                _terminal.BackgroundColor = originalBackground;
                _terminal.ResetColor();
            }
        }

        private bool TryWriteTrailingNewLine()
        {
            try
            {
                _terminal.WriteLine();
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private int CalculateRealtimeEstimate(TimeSpan elapsed)
        {
            int growth = (int)Math.Ceiling(Math.Max(0d, elapsed.TotalSeconds) * EstimatedTokensPerSecond);
            return Math.Max(1, _sessionSeedEstimatedTokens + growth);
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            int seconds = Math.Max(1, (int)Math.Floor(Math.Max(1, elapsed.TotalSeconds)));
            return $"{seconds}s";
        }
    }
}
