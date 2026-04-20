using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.ConsoleHost.Rendering;
using NanoAgent.ConsoleHost.Terminal;
using Spectre.Console;

namespace NanoAgent.ConsoleHost.Repl;

internal sealed class ConsoleReplOutputWriter : IReplOutputWriter
{
    private const double EstimatedTokensPerSecond = 4d;
    private const int HeaderDividerWidth = 53;
    private const string RepositoryUrl = "github.com/rizwan3d/NanoAgent";
    private const string SponsorName = "ALFAIN Technologies (PVT) Limited";
    private const string SponsorUrl = "https://alfain.co/";

    private readonly ICliMessageFormatter _formatter;
    private readonly ICliTextRenderer _renderer;
    private readonly ICliOutputTarget _outputTarget;
    private readonly IAnsiConsole _console;
    private readonly IConsoleInteractionGate _interactionGate;
    private readonly IConsoleTerminal _terminal;
    private readonly ConsoleRenderSettings _renderSettings;

    public ConsoleReplOutputWriter(
        ICliMessageFormatter formatter,
        ICliTextRenderer renderer,
        ICliOutputTarget outputTarget,
        IAnsiConsole console,
        IConsoleInteractionGate interactionGate,
        IConsoleTerminal terminal,
        ConsoleRenderSettings renderSettings)
    {
        _formatter = formatter;
        _renderer = renderer;
        _outputTarget = outputTarget;
        _console = console;
        _interactionGate = interactionGate;
        _terminal = terminal;
        _renderSettings = renderSettings;
    }

    public Task WriteErrorAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _renderer.RenderAsync(
            _formatter.Format(CliRenderMessageKind.Error, message),
            cancellationToken);
    }

    public ValueTask<IResponseProgress> BeginResponseProgressAsync(
        int estimatedOutputTokens,
        int completedSessionEstimatedOutputTokens,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_terminal.IsOutputRedirected)
        {
            return ValueTask.FromResult<IResponseProgress>(NoOpResponseProgress.Instance);
        }

        return ValueTask.FromResult<IResponseProgress>(
            new ProgressScope(
                _terminal,
                _outputTarget,
                _interactionGate,
                estimatedOutputTokens,
                completedSessionEstimatedOutputTokens));
    }



    public async Task WriteShellHeaderAsync(
        string applicationName,
        string modelName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        _outputTarget.WriteLine();

        await WriteNanoAgentHeaderAsync(cancellationToken).ConfigureAwait(false);

        _outputTarget.WriteLine([
            new CliOutputSegment("  Model: ", CliOutputStyle.Muted),
            new CliOutputSegment(modelName.Trim(), CliOutputStyle.InlineCode)
        ]);
        await DelayIfAnimatedAsync(cancellationToken).ConfigureAwait(false);
        _outputTarget.WriteLine([
            new CliOutputSegment("  GitHub: ", CliOutputStyle.Muted),
            new CliOutputSegment(RepositoryUrl, CliOutputStyle.Info)
        ]);
        await DelayIfAnimatedAsync(cancellationToken).ConfigureAwait(false);
        _outputTarget.WriteLine([
            new CliOutputSegment("  Sponsor: ", CliOutputStyle.Muted),
            new CliOutputSegment(SponsorName, CliOutputStyle.Warning),
            new CliOutputSegment(" ", CliOutputStyle.Muted),
            new CliOutputSegment($"({SponsorUrl})", CliOutputStyle.Emphasis)
        ]);
        await DelayIfAnimatedAsync(cancellationToken).ConfigureAwait(false);
        _outputTarget.WriteLine([
            new CliOutputSegment("  ", CliOutputStyle.Muted),
            new CliOutputSegment(new string('\u2500', HeaderDividerWidth), CliOutputStyle.CodeFence)
        ]);
        await DelayIfAnimatedAsync(cancellationToken).ConfigureAwait(false);
        _outputTarget.WriteLine([
            new CliOutputSegment(
                "  Chat in the terminal. Press Ctrl+C or use /exit to quit.",
                CliOutputStyle.Muted)
        ]);
        await DelayIfAnimatedAsync(cancellationToken).ConfigureAwait(false);
        _outputTarget.WriteLine([
            new CliOutputSegment(
                "  Press Esc while a response is running to interrupt the current request.",
                CliOutputStyle.Muted)
        ]);
        _outputTarget.WriteLine();
    }

    private async Task WriteNanoAgentHeaderAsync(CancellationToken cancellationToken)
    {
        (string Nano, string Agent)[] wordmark =
        [
            (
            "███╗   ██╗  █████╗  ███╗   ██╗  ██████╗",
            "  █████╗   ██████╗  ███████╗  ███╗   ██╗  ████████╗"
        ),
        (
            "████╗  ██║ ██╔══██╗ ████╗  ██║ ██╔═══██╗",
            " ██╔══██╗ ██╔════╝  ██╔════╝  ████╗  ██║  ╚══██╔══╝"
        ),
        (
            "██╔██╗ ██║ ███████║ ██╔██╗ ██║ ██║   ██║",
            " ███████║ ██║  ███╗ █████╗    ██╔██╗ ██║     ██║"
        ),
        (
            "██║╚██╗██║ ██╔══██║ ██║╚██╗██║ ██║   ██║",
            " ██╔══██║ ██║   ██║ ██╔══╝    ██║╚██╗██║     ██║"
        ),
        (
            "██║ ╚████║ ██║  ██║ ██║ ╚████║ ╚██████╔╝",
            " ██║  ██║ ╚██████╔╝ ███████╗  ██║ ╚████║     ██║"
        ),
        (
            "╚═╝  ╚═══╝ ╚═╝  ╚═╝ ╚═╝  ╚═══╝  ╚═════╝",
            "  ╚═╝  ╚═╝  ╚═════╝  ╚══════╝  ╚═╝  ╚═══╝     ╚═╝"
        )
        ];

        for (int i = 0; i < wordmark.Length; i++)
        {
            string accentColor = i < 3 ? "fuchsia" : "purple";
            _console.Write(new Markup(
                $"[grey]  [/][{accentColor}]   [/][white]{Markup.Escape(wordmark[i].Nano)}[/][fuchsia]{Markup.Escape(wordmark[i].Agent)}[/]"));
            _console.WriteLine();
            await DelayIfAnimatedAsync(cancellationToken).ConfigureAwait(false);
        }

        _outputTarget.WriteLine();
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

    private async Task DelayIfAnimatedAsync(CancellationToken cancellationToken)
    {
        if (!_renderSettings.EnableAnimations ||
            _terminal.IsOutputRedirected ||
            _renderSettings.HeaderLineDelay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(_renderSettings.HeaderLineDelay, cancellationToken).ConfigureAwait(false);
    }

    private sealed class NoOpResponseProgress : IResponseProgress
    {
        public static NoOpResponseProgress Instance { get; } = new();

        public Task ReportExecutionPlanAsync(
            ExecutionPlanProgress executionPlanProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(executionPlanProgress);
            return Task.CompletedTask;
        }

        public Task ReportToolCallsStartedAsync(
            IReadOnlyList<ConversationToolCall> toolCalls,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ReportToolResultsAsync(
            ToolExecutionBatchResult toolExecutionResult,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProgressScope : IResponseProgress
    {
        private readonly IConsoleTerminal _terminal;
        private readonly ICliOutputTarget _outputTarget;
        private readonly IConsoleInteractionGate _interactionGate;
        private readonly int _sessionSeedEstimatedTokens;
        private readonly object _syncLock = new();
        private ExecutionPlanProgress? _executionPlanProgress;
        private int _statusBlockLineCount;
        private int _statusLineTop;
        private DateTimeOffset _startedAt;

        public ProgressScope(
            IConsoleTerminal terminal,
            ICliOutputTarget outputTarget,
            IConsoleInteractionGate interactionGate,
            int estimatedOutputTokens,
            int completedSessionEstimatedOutputTokens)
        {
            _terminal = terminal;
            _outputTarget = outputTarget;
            _interactionGate = interactionGate;
            _sessionSeedEstimatedTokens = Math.Max(0, completedSessionEstimatedOutputTokens) +
                Math.Max(1, estimatedOutputTokens);
            _statusLineTop = terminal.CursorTop;
            _statusBlockLineCount = 1;
            _startedAt = DateTimeOffset.UtcNow;

            TryWriteStatusBlock(TimeSpan.Zero);
        }

        public async ValueTask DisposeAsync()
        {
            await ValueTask.CompletedTask.ConfigureAwait(false);
            ClearStatusBlock();
            TrySetCursorPosition(0, _statusLineTop);
        }

        public Task ReportExecutionPlanAsync(
            ExecutionPlanProgress executionPlanProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(executionPlanProgress);

            lock (_syncLock)
            {
                _executionPlanProgress = executionPlanProgress;
                ClearStatusBlock();
                TryWriteStatusBlock(DateTimeOffset.UtcNow - _startedAt);
            }

            return Task.CompletedTask;
        }

        public Task ReportToolCallsStartedAsync(
            IReadOnlyList<ConversationToolCall> toolCalls,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(toolCalls);

            return Task.CompletedTask;
        }

        public Task ReportToolResultsAsync(
            ToolExecutionBatchResult toolExecutionResult,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(toolExecutionResult);

            if (toolExecutionResult.Results.Count == 0)
            {
                return Task.CompletedTask;
            }

            lock (_syncLock)
            {
                ClearStatusBlock();
                TrySetCursorPosition(0, _statusLineTop);

                WriteToolOutputs(toolExecutionResult);

                _statusLineTop = _terminal.CursorTop;
                TryWriteStatusBlock(DateTimeOffset.UtcNow - _startedAt);
            }

            return Task.CompletedTask;
        }

        private void ClearStatusBlock()
        {
            int width = Math.Max(1, _terminal.WindowWidth - 1);

            try
            {
                using IDisposable _ = _interactionGate.EnterScope();
                for (int offset = 0; offset < _statusBlockLineCount; offset++)
                {
                    _terminal.SetCursorPosition(0, _statusLineTop + offset);
                    _terminal.Write(new string(' ', width));
                }

                _terminal.SetCursorPosition(0, _statusLineTop);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            catch (IOException)
            {
            }
        }

        private string BuildStatusLine(TimeSpan elapsed)
        {
            const string workingLabel = "Working";
            string[] spinnerFrames = ["|", "/", "-", "\\"];
            int spinnerFrameIndex = (int)Math.Max(0d, elapsed.TotalSeconds) %
                                    spinnerFrames.Length;
            int highlightedCharacterIndex = (int)Math.Max(0d, elapsed.TotalSeconds) %
                                            workingLabel.Length;

            List<string> segments = [];
            segments.Add(spinnerFrames[spinnerFrameIndex]);
            segments.Add(" ");

            for (int index = 0; index < workingLabel.Length; index++)
            {
                segments.Add(workingLabel[index].ToString());
            }

            segments.Add($" {MetricDisplayFormatter.FormatEstimatedOutputMetric(elapsed, CalculateRealtimeEstimate(elapsed))}");
            segments.Add("  Esc to interrupt");

            return string.Concat(segments);
        }

        private IReadOnlyList<string> BuildStatusBlockLines(TimeSpan elapsed)
        {
            List<string> lines = [];
            lines.AddRange(BuildExecutionPlanLines());
            lines.Add(BuildStatusLine(elapsed));
            return lines;
        }

        private IReadOnlyList<string> BuildExecutionPlanLines()
        {
            if (_executionPlanProgress is null || _executionPlanProgress.Tasks.Count == 0)
            {
                return [];
            }

            const int maxVisibleTasks = 4;
            ExecutionPlanProgress progress = _executionPlanProgress;
            int taskCount = progress.Tasks.Count;
            int currentTaskIndex = progress.CurrentTaskIndex;
            int startIndex = currentTaskIndex switch
            {
                < 0 => Math.Max(0, taskCount - maxVisibleTasks),
                <= 1 => 0,
                _ => Math.Min(taskCount - maxVisibleTasks, currentTaskIndex - 1)
            };
            int endIndexExclusive = Math.Min(taskCount, startIndex + maxVisibleTasks);

            List<string> lines =
            [
                $"Tasks: {progress.CompletedTaskCount} done | {progress.CurrentTaskCount} current | {progress.RemainingTaskCount} remaining"
            ];

            if (startIndex > 0)
            {
                lines.Add($"  ... +{startIndex} earlier {(startIndex == 1 ? "task" : "tasks")}");
            }

            for (int index = startIndex; index < endIndexExclusive; index++)
            {
                string prefix = index < progress.CompletedTaskCount
                    ? "[x]"
                    : index == currentTaskIndex
                        ? "[>]"
                        : "[ ]";

                lines.Add($"  {prefix} {progress.Tasks[index]}");
            }

            if (endIndexExclusive < taskCount)
            {
                int remainingCount = taskCount - endIndexExclusive;
                lines.Add($"  ... +{remainingCount} more {(remainingCount == 1 ? "task" : "tasks")}");
            }

            return lines;
        }

        private bool TryWriteStatusBlock(TimeSpan elapsed)
        {
            int width = Math.Max(1, _terminal.WindowWidth - 1);
            IReadOnlyList<string> lines = BuildStatusBlockLines(elapsed)
                .Select(line => FitStatusText(line, width))
                .ToArray();

            try
            {
                using IDisposable _ = _interactionGate.EnterScope();
                _terminal.SetCursorPosition(0, _statusLineTop);

                for (int index = 0; index < lines.Count; index++)
                {
                    string paddedLine = lines[index].PadRight(width);
                    if (index < lines.Count - 1)
                    {
                        _terminal.WriteLine(paddedLine);
                    }
                    else
                    {
                        _terminal.Write(paddedLine);
                    }
                }

                _terminal.SetCursorPosition(0, _statusLineTop);
                _statusBlockLineCount = Math.Max(1, lines.Count);

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
        }

        private static string FitStatusText(
            string text,
            int width)
        {
            if (width <= 0)
            {
                return string.Empty;
            }

            if (text.Length <= width)
            {
                return text;
            }

            if (width <= 3)
            {
                return new string('.', width);
            }

            return text[..(width - 3)] + "...";
        }

        private void WriteToolOutputs(ToolExecutionBatchResult toolExecutionResult)
        {
            List<ToolInvocationResult> fileWriteBatch = [];
            bool hasWrittenToolOutput = false;

            foreach (ToolInvocationResult invocationResult in toolExecutionResult.Results)
            {
                if (CanGroupFileWrite(invocationResult))
                {
                    fileWriteBatch.Add(invocationResult);
                    continue;
                }

                hasWrittenToolOutput = FlushFileWriteBatch(fileWriteBatch, hasWrittenToolOutput);

                if (hasWrittenToolOutput)
                {
                    _outputTarget.WriteLine();
                }

                WriteToolOutput(invocationResult);
                hasWrittenToolOutput = true;
            }

            FlushFileWriteBatch(fileWriteBatch, hasWrittenToolOutput);
        }

        private bool FlushFileWriteBatch(
            List<ToolInvocationResult> fileWriteBatch,
            bool hasWrittenToolOutput)
        {
            if (fileWriteBatch.Count == 0)
            {
                return hasWrittenToolOutput;
            }

            List<WorkspaceFileWriteResult> results = fileWriteBatch
                .Select(static invocationResult => DeserializeWorkspaceFileWriteResult(invocationResult.Result.JsonResult))
                .Where(static result => result is not null)
                .Cast<WorkspaceFileWriteResult>()
                .ToList();

            if (results.Count == 0)
            {
                foreach (ToolInvocationResult invocationResult in fileWriteBatch)
                {
                    if (hasWrittenToolOutput)
                    {
                        _outputTarget.WriteLine();
                    }

                    WriteGenericToolOutput(invocationResult);
                    hasWrittenToolOutput = true;
                }

                fileWriteBatch.Clear();
                return hasWrittenToolOutput;
            }

            if (hasWrittenToolOutput)
            {
                _outputTarget.WriteLine();
            }

            int totalAddedLineCount = results.Sum(static result => result.AddedLineCount);
            int totalRemovedLineCount = results.Sum(static result => result.RemovedLineCount);

            WriteToolTitle(
                $"Edited {results.Count} {(results.Count == 1 ? "file" : "files")} (+{totalAddedLineCount} -{totalRemovedLineCount})",
                CliOutputStyle.Info);

            WorkspaceFileWriteResult primaryResult = results[0];
            _outputTarget.WriteLine([
                new CliOutputSegment("  └ ", CliOutputStyle.Muted),
                new CliOutputSegment(primaryResult.Path, CliOutputStyle.InlineCode),
                new CliOutputSegment(
                    $" (+{primaryResult.AddedLineCount} -{primaryResult.RemovedLineCount})",
                    CliOutputStyle.Muted)
            ]);

            foreach (WorkspaceFileWritePreviewLine previewLine in primaryResult.PreviewLines ?? [])
            {
                CliOutputStyle previewStyle = previewLine.Kind switch
                {
                    "add" => CliOutputStyle.DiffAddition,
                    "remove" => CliOutputStyle.DiffRemoval,
                    _ => CliOutputStyle.DiffContext
                };

                string indicator = previewLine.Kind switch
                {
                    "add" => "+",
                    "remove" => "-",
                    _ => " "
                };

                _outputTarget.WriteLine([
                    new CliOutputSegment("      ", CliOutputStyle.Muted),
                    new CliOutputSegment(previewLine.LineNumber.ToString().PadLeft(4), CliOutputStyle.Muted),
                    new CliOutputSegment(" ", CliOutputStyle.Muted),
                    new CliOutputSegment(indicator, previewStyle),
                    new CliOutputSegment(previewLine.Text, previewStyle)
                ]);
            }

            if (primaryResult.RemainingPreviewLineCount > 0)
            {
                WriteContinuationLine(
                    $"\u2026 +{primaryResult.RemainingPreviewLineCount} lines",
                    CliOutputStyle.Muted);
            }

            if (results.Count > 1)
            {
                WriteContinuationLine(
                    $"\u2026 +{results.Count - 1} more {(results.Count == 2 ? "file" : "files")}",
                    CliOutputStyle.Muted);
            }

            fileWriteBatch.Clear();
            return true;
        }

        private void WriteToolOutput(ToolInvocationResult invocationResult)
        {
            if (TryWriteShellCommandOutput(invocationResult) ||
                TryWriteFileReadOutput(invocationResult) ||
                TryWriteDirectoryListOutput(invocationResult) ||
                TryWriteTextSearchOutput(invocationResult))
            {
                return;
            }

            WriteGenericToolOutput(invocationResult);
        }

        private bool TryWriteShellCommandOutput(ToolInvocationResult invocationResult)
        {
            if (!string.Equals(invocationResult.ToolName, AgentToolNames.ShellCommand, StringComparison.Ordinal))
            {
                return false;
            }

            ShellCommandExecutionResult? result = DeserializeShellCommandExecutionResult(invocationResult.Result.JsonResult);
            if (result is null)
            {
                return false;
            }

            CliOutputStyle titleStyle = result.ExitCode switch
            {
                0 when !string.IsNullOrWhiteSpace(result.StandardError) => CliOutputStyle.Warning,
                0 => CliOutputStyle.Info,
                _ => CliOutputStyle.Error
            };

            WriteToolTitle($"Ran {result.Command}", titleStyle);

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                WritePreviewBlock(
                    result.StandardError,
                    result.ExitCode == 0 ? CliOutputStyle.Warning : CliOutputStyle.Error);
            }
            else if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                WritePreviewBlock(result.StandardOutput, CliOutputStyle.AssistantText);
            }
            else
            {
                WritePreviewBlock(
                    $"Exit code: {result.ExitCode}",
                    result.ExitCode == 0 ? CliOutputStyle.Muted : CliOutputStyle.Error);
            }

            return true;
        }

        private bool TryWriteFileReadOutput(ToolInvocationResult invocationResult)
        {
            if (!string.Equals(invocationResult.ToolName, AgentToolNames.FileRead, StringComparison.Ordinal))
            {
                return false;
            }

            WorkspaceFileReadResult? result = DeserializeWorkspaceFileReadResult(invocationResult.Result.JsonResult);
            if (result is null)
            {
                return false;
            }

            WriteToolTitle($"Read {result.Path}", CliOutputStyle.Info);
            WritePreviewBlock(result.Content ?? string.Empty, CliOutputStyle.CodeText);
            return true;
        }

        private bool TryWriteDirectoryListOutput(ToolInvocationResult invocationResult)
        {
            if (!string.Equals(invocationResult.ToolName, AgentToolNames.DirectoryList, StringComparison.Ordinal))
            {
                return false;
            }

            WorkspaceDirectoryListResult? result = DeserializeWorkspaceDirectoryListResult(invocationResult.Result.JsonResult);
            if (result is null)
            {
                return false;
            }

            WriteToolTitle($"Listed {result.Path}", CliOutputStyle.Info);
            IReadOnlyList<WorkspaceDirectoryEntry> entries = result.Entries ?? [];
            string listing = entries.Count == 0
                ? "(empty)"
                : string.Join(
                    Environment.NewLine,
                    entries.Select(static entry => $"{entry.EntryType}: {entry.Path}"));
            WritePreviewBlock(listing, CliOutputStyle.AssistantText);
            return true;
        }

        private bool TryWriteTextSearchOutput(ToolInvocationResult invocationResult)
        {
            if (!string.Equals(invocationResult.ToolName, AgentToolNames.TextSearch, StringComparison.Ordinal))
            {
                return false;
            }

            WorkspaceTextSearchResult? result = DeserializeWorkspaceTextSearchResult(invocationResult.Result.JsonResult);
            if (result is null)
            {
                return false;
            }

            WriteToolTitle($"Searched {result.Path} for \"{result.Query}\"", CliOutputStyle.Info);

            IReadOnlyList<WorkspaceTextSearchMatch> matches = result.Matches ?? [];
            string preview = matches.Count == 0
                ? "(no matches)"
                : string.Join(
                    Environment.NewLine,
                    matches.Select(static match => $"{match.Path}:{match.LineNumber} {match.LineText}"));

            WritePreviewBlock(preview, CliOutputStyle.AssistantText);
            return true;
        }

        private void WriteGenericToolOutput(ToolInvocationResult invocationResult)
        {
            CliOutputStyle titleStyle = invocationResult.Result.IsSuccess
                ? CliOutputStyle.Info
                : invocationResult.Result.Status == ToolResultStatus.PermissionDenied ||
                  invocationResult.Result.Status == ToolResultStatus.InvalidArguments
                    ? CliOutputStyle.Warning
                    : CliOutputStyle.Error;

            ToolRenderPayload? renderPayload = invocationResult.Result.RenderPayload;
            string title = renderPayload?.Title ?? $"Tool output: {invocationResult.ToolName}";
            string text = renderPayload?.Text ?? invocationResult.Result.Message;

            WriteToolTitle(title, titleStyle);
            WritePreviewBlock(text, invocationResult.Result.IsSuccess ? CliOutputStyle.AssistantText : titleStyle);
        }

        private void WriteToolTitle(string title, CliOutputStyle style)
        {
            _outputTarget.WriteLine([
                new CliOutputSegment("\u2022 ", style),
                new CliOutputSegment(title, style)
            ]);
        }

        private void WritePreviewBlock(string text, CliOutputStyle style)
        {
            string[] lines = NormalizePreviewLines(text);
            if (lines.Length == 0)
            {
                WriteBranchLine("(no output)", CliOutputStyle.Muted, isFirstLine: true);
                return;
            }

            const int maxLines = 4;
            int displayedLineCount = Math.Min(maxLines, lines.Length);

            for (int index = 0; index < displayedLineCount; index++)
            {
                WriteBranchLine(lines[index], style, isFirstLine: index == 0);
            }

            if (lines.Length > displayedLineCount)
            {
                WriteContinuationLine(
                    $"\u2026 +{lines.Length - displayedLineCount} lines",
                    CliOutputStyle.Muted);
            }
        }

        private void WriteBranchLine(
            string text,
            CliOutputStyle style,
            bool isFirstLine)
        {
            _outputTarget.WriteLine([
                new CliOutputSegment(isFirstLine ? "  └ " : "    ", CliOutputStyle.Muted),
                new CliOutputSegment(text, style)
            ]);
        }

        private void WriteContinuationLine(string text, CliOutputStyle style)
        {
            _outputTarget.WriteLine([
                new CliOutputSegment("    ", CliOutputStyle.Muted),
                new CliOutputSegment(text, style)
            ]);
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

        private static bool CanGroupFileWrite(ToolInvocationResult invocationResult)
        {
            return invocationResult.Result.IsSuccess &&
                   string.Equals(invocationResult.ToolName, AgentToolNames.FileWrite, StringComparison.Ordinal) &&
                   DeserializeWorkspaceFileWriteResult(invocationResult.Result.JsonResult) is not null;
        }

        private static string[] NormalizeLines(string text)
        {
            return text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.None);
        }

        private static string[] NormalizePreviewLines(string text)
        {
            return NormalizeLines(text)
                .Select(static line => line.TrimEnd())
                .SkipWhile(static line => string.IsNullOrWhiteSpace(line))
                .Reverse()
                .SkipWhile(static line => string.IsNullOrWhiteSpace(line))
                .Reverse()
                .ToArray();
        }

        private static WorkspaceFileWriteResult? DeserializeWorkspaceFileWriteResult(string json)
        {
            return TryDeserialize(json, ToolJsonContext.Default.WorkspaceFileWriteResult);
        }

        private static ShellCommandExecutionResult? DeserializeShellCommandExecutionResult(string json)
        {
            return TryDeserialize(json, ToolJsonContext.Default.ShellCommandExecutionResult);
        }

        private static WorkspaceFileReadResult? DeserializeWorkspaceFileReadResult(string json)
        {
            return TryDeserialize(json, ToolJsonContext.Default.WorkspaceFileReadResult);
        }

        private static WorkspaceDirectoryListResult? DeserializeWorkspaceDirectoryListResult(string json)
        {
            return TryDeserialize(json, ToolJsonContext.Default.WorkspaceDirectoryListResult);
        }

        private static WorkspaceTextSearchResult? DeserializeWorkspaceTextSearchResult(string json)
        {
            return TryDeserialize(json, ToolJsonContext.Default.WorkspaceTextSearchResult);
        }

        private static TPayload? TryDeserialize<TPayload>(
            string json,
            JsonTypeInfo<TPayload> typeInfo)
        {
            try
            {
                return JsonSerializer.Deserialize(json, typeInfo);
            }
            catch (JsonException)
            {
                return default;
            }
        }

        private void TrySetCursorPosition(int left, int top)
        {
            try
            {
                _terminal.SetCursorPosition(left, top);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            catch (IOException)
            {
            }
        }
    }
}
