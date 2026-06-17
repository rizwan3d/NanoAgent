using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;

namespace NanoAgent.CLI;

public static partial class Program
{
    private const string TerminalsViewCommand = "/terminals view";
    private const string RunningTerminalStatus = "running";
    private const string NotFoundTerminalStatus = "not_found";

    // Returns true when the input is a `/terminals view [id]` command. Plain
    // `/terminals` and `/terminals stop ...` are intentionally left to the normal
    // command dispatcher (TerminalsCommandHandler).
    private static bool TryHandleTerminalView(AppState state, string command)
    {
        if (!command.Equals(TerminalsViewCommand, StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith(TerminalsViewCommand + " ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string terminalId = command.Length > TerminalsViewCommand.Length
            ? command[TerminalsViewCommand.Length..].Trim()
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(terminalId))
        {
            StartTerminalAttach(state, terminalId);
        }
        else
        {
            StartTerminalPicker(state);
        }

        return true;
    }

    // Lists the session's background terminals and either reports that none are
    // running, attaches directly when there is a single one, or shows a picker.
    private static void StartTerminalPicker(AppState state)
    {
        state.ResetTurnCancellation();
        state.TurnCancellation = CancellationTokenSource.CreateLinkedTokenSource(state.LifetimeCancellation.Token);

        state.IsBusy = true;
        state.ActivityText = "Listing terminals";

        CancellationToken cancellationToken = state.TurnCancellation.Token;

        state.ActiveOperation = Task.Run(async () =>
        {
            try
            {
                IReadOnlyList<BackgroundTerminalInfo> terminals =
                    await state.Backend.ListBackgroundTerminalsAsync(cancellationToken);

                state.UiBridge.Enqueue(appState =>
                {
                    appState.IsBusy = false;
                    appState.ActivityText = appState.IsReady ? "Ready" : "Idle";
                    appState.ResetTurnCancellation();

                    BackgroundTerminalInfo[] running = terminals
                        .Where(static terminal => string.Equals(terminal.Status, RunningTerminalStatus, StringComparison.Ordinal))
                        .ToArray();

                    if (running.Length == 0)
                    {
                        appState.AddSystemMessage("No running background terminals to view. Start one with !!<command>.");
                        TryStartNextPendingSubmission(appState);
                        return;
                    }

                    if (running.Length == 1)
                    {
                        StartTerminalAttach(appState, running[0].Id);
                        return;
                    }

                    ShowTerminalPickerModal(appState, running);
                });
            }
            catch (OperationCanceledException) when (state.LifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException) when (state.TurnCancellation?.IsCancellationRequested == true)
            {
                state.UiBridge.Enqueue(appState =>
                {
                    appState.IsBusy = false;
                    appState.ActivityText = appState.IsReady ? "Ready" : "Idle";
                    appState.ResetTurnCancellation();
                    TryStartNextPendingSubmission(appState);
                });
            }
            catch (Exception exception)
            {
                state.UiBridge.Enqueue(appState =>
                {
                    appState.IsBusy = false;
                    appState.ActivityText = appState.IsReady ? "Ready" : "Idle";
                    appState.AddSystemMessage($"Failed to list background terminals: {exception.Message}");
                    TryStartNextPendingSubmission(appState);
                });
            }
        });
    }

    private static void ShowTerminalPickerModal(
        AppState state,
        IReadOnlyList<BackgroundTerminalInfo> running)
    {
        SelectionPromptOption<string>[] options = running
            .Select(static terminal => new SelectionPromptOption<string>(
                terminal.Id,
                terminal.Id,
                $"{terminal.Status} · {DescribeTerminalCommand(terminal.Command)} · cwd {terminal.WorkingDirectory}"))
            .ToArray();

        state.ActiveModal = SelectionModalState<string>.Create(
            new SelectionPromptRequest<string>(
                "Select a background terminal to view",
                options,
                "Stream a running background terminal's output (press Esc to cancel).",
                DefaultIndex: 0,
                AllowCancellation: true),
            completionToken: new object(),
            onSelected: id => StartTerminalAttach(state, id),
            onCancelled: _ => state.AddSystemMessage("Cancelled terminal view."));
    }

    // Attaches to a background terminal and streams its new output live until it
    // exits or the user presses Esc to detach (leaving it running). Mirrors
    // StartConversation's busy/cancellation lifecycle.
    private static void StartTerminalAttach(AppState state, string terminalId)
    {
        string normalizedId = terminalId.Trim();
        if (string.IsNullOrEmpty(normalizedId))
        {
            state.AddSystemMessage("Usage: /terminals view <terminal-id>");
            return;
        }

        state.ResetTurnCancellation();
        state.TurnCancellation = CancellationTokenSource.CreateLinkedTokenSource(state.LifetimeCancellation.Token);

        state.IsBusy = true;
        state.CurrentTurnStartedAt = DateTimeOffset.UtcNow;
        state.ActivityText = $"Viewing {normalizedId}";
        state.UiBridge.ResetAssistantMessageChunkTracking();
        state.AddSystemMessage(
            $"Attaching to background terminal {normalizedId}. Streaming new output (press Esc to detach; it keeps running).");

        CancellationToken cancellationToken = state.TurnCancellation.Token;

        state.ActiveOperation = Task.Run(async () =>
        {
            try
            {
                ShellCommandExecutionResult? finalResult = null;

                while (true)
                {
                    await Task.Delay(200, cancellationToken);

                    ShellCommandExecutionResult readResult = await state.Backend.ReadBackgroundTerminalAsync(
                        normalizedId,
                        cancellationToken);

                    if (string.Equals(readResult.TerminalStatus, NotFoundTerminalStatus, StringComparison.Ordinal))
                    {
                        finalResult = readResult;
                        break;
                    }

                    // Header prefixed before each chunk of live output, to label
                    // which background terminal and command it belongs to. The
                    // command is read from the poll result.
                    string streamHeader = $"{normalizedId} - {readResult.Command}{Environment.NewLine}";

                    if (!string.IsNullOrEmpty(readResult.StandardOutput))
                    {
                        state.UiBridge.ShowAssistantMessageChunk(streamHeader + readResult.StandardOutput);
                    }

                    if (!string.IsNullOrEmpty(readResult.StandardError))
                    {
                        state.UiBridge.ShowAssistantMessageChunk(streamHeader + readResult.StandardError);
                    }

                    if (!string.Equals(readResult.TerminalStatus, RunningTerminalStatus, StringComparison.Ordinal))
                    {
                        finalResult = readResult;
                        break;
                    }
                }

                ShellCommandExecutionResult completed = finalResult!;
                state.UiBridge.Enqueue(appState =>
                {
                    appState.CurrentTurnStartedAt = null;
                    FinishTerminalAttach(appState);

                    string message = string.Equals(completed.TerminalStatus, NotFoundTerminalStatus, StringComparison.Ordinal)
                        ? $"Background terminal '{normalizedId}' was not found (it may have stopped and expired)."
                        : $"Background terminal {normalizedId} {completed.TerminalStatus} (exit code {completed.ExitCode}).";
                    appState.AddSystemMessage(message);
                });
            }
            catch (OperationCanceledException) when (state.LifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException) when (state.TurnCancellation?.IsCancellationRequested == true)
            {
                state.UiBridge.Enqueue(appState =>
                {
                    appState.CurrentTurnStartedAt = null;
                    FinishTerminalAttach(appState);
                    appState.ResetTurnCancellation();
                    appState.AddSystemMessage(
                        $"Detached from background terminal {normalizedId}. It is still running; re-attach with /terminals view {normalizedId}.");
                });
            }
            catch (Exception exception)
            {
                state.UiBridge.Enqueue(appState =>
                {
                    appState.CurrentTurnStartedAt = null;
                    FinishTerminalAttach(appState);
                    appState.AddSystemMessage($"Failed to view terminal {normalizedId}: {exception.Message}");
                });
            }
        });
    }

    // Clears the busy state once any streamed output has finished draining.
    private static void FinishTerminalAttach(AppState state)
    {
        if (state.IsStreaming || state.StreamQueue.Count > 0)
        {
            state.ClearBusyWhenStreamCompletes = true;
            return;
        }

        state.IsBusy = false;
        state.ClearBusyWhenStreamCompletes = false;
        state.ActivityText = state.IsReady ? "Ready" : "Idle";
        TryStartNextPendingSubmission(state);
    }

    private static string DescribeTerminalCommand(string command)
    {
        string singleLine = command
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        const int maxLength = 60;
        return singleLine.Length <= maxLength
            ? singleLine
            : singleLine[..maxLength] + "…";
    }
}
