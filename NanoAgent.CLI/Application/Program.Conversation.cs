using NanoAgent.Application.Models;

namespace NanoAgent.CLI;

public static partial class Program
{
    private static void UpdateModal(AppState state)
    {
        state.ActiveModal?.Update(state);
    }

    private static void SubmitInput(AppState state)
    {
        string text = state.Input.ToString().Trim();
        state.Input.Clear();

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.StartsWith('/'))
        {
            HandleCommand(state, text);
            return;
        }

        if (!state.IsReady)
        {
            state.AddSystemMessage(
                state.HasFatalError
                    ? "NanoAgent backend failed to start. Use /exit and try again."
                    : "NanoAgent is still starting up. Please wait.");
            return;
        }

        if (state.IsBusy || state.IsStreaming)
        {
            state.AddSystemMessage("NanoAgent is still working on the current request.");
            return;
        }

        state.ConversationScrollOffset = 0;
        state.AddMessage(Role.User, text);
        StartConversation(state, text);
    }

    private static void StartConversation(AppState state, string prompt)
    {
        state.IsBusy = true;
        state.ClearBusyWhenStreamCompletes = false;
        state.CurrentTurnStartedAt = DateTimeOffset.UtcNow;
        state.PendingCompletionNote = null;
        state.ActivityText = "Thinking";

        state.ActiveOperation = Task.Run(async () =>
        {
            try
            {
                ConversationTurnResult result = await state.Backend.RunTurnAsync(
                    prompt,
                    state.UiBridge,
                    state.LifetimeCancellation.Token);

                state.UiBridge.Enqueue(appState =>
                {
                    string completionNote = result.Metrics is null
                        ? string.Empty
                        : FormatCompletionNote(
                            result.Metrics.Elapsed,
                            result.Metrics.DisplayedEstimatedOutputTokens);

                    appState.PendingCompletionNote = string.IsNullOrWhiteSpace(completionNote)
                        ? null
                        : completionNote;
                    appState.CurrentTurnStartedAt = null;

                    if (!string.IsNullOrWhiteSpace(result.ResponseText))
                    {
                        appState.BeginAssistantStream(result.ResponseText);
                        appState.ClearBusyWhenStreamCompletes = true;
                        appState.ActivityText = "Streaming response";
                        return;
                    }

                    appState.IsBusy = false;
                    appState.ActivityText = appState.IsReady ? "Ready" : "Idle";
                });
            }
            catch (OperationCanceledException) when (state.LifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                state.UiBridge.Enqueue(appState =>
                {
                    appState.IsBusy = false;
                    appState.ClearBusyWhenStreamCompletes = false;
                    appState.CurrentTurnStartedAt = null;
                    appState.PendingCompletionNote = null;
                    appState.ActivityText = appState.IsReady ? "Ready" : "Idle";
                    appState.AddSystemMessage($"NanoAgent error: {exception.Message}");
                });
            }
        });
    }

    private static void HandleCommand(AppState state, string command)
    {
        if (command == "/exit")
        {
            state.Running = false;
            return;
        }

        if ((state.IsBusy || state.IsStreaming) && command != "/help")
        {
            state.AddSystemMessage("That command is unavailable while NanoAgent is working.");
            return;
        }

        if (command == "/clear")
        {
            state.Messages.Clear();
            state.ConversationScrollOffset = 0;
            state.CurrentTurnStartedAt = null;
            state.PendingCompletionNote = null;
            state.AddSystemMessage("Screen cleared.");
            return;
        }

        if (command == "/help")
        {
            state.AddSystemMessage(
                """
                Commands:
                  /help          Show help
                  /clear         Clear messages
                  /exit          Quit
                  /ls            List files in workspace
                  /read <file>   Ask permission, then read file

                Natural-language prompts are sent to the NanoAgent backend.

                Prompt controls:
                  Up / Down      Change selection
                  Enter          Confirm
                  Esc            Cancel when allowed
                  Mouse wheel    Scroll conversation
                  PgUp / PgDn    Scroll conversation
                  Home / End     Jump conversation
                """);
            return;
        }

        if (command == "/ls")
        {
            ExecuteListFiles(state);
            return;
        }

        if (command.StartsWith("/read ", StringComparison.Ordinal))
        {
            string path = command["/read ".Length..].Trim();

            if (string.IsNullOrWhiteSpace(path))
            {
                state.AddSystemMessage("Usage: /read <file>");
                return;
            }

            RequestReadPermission(state, path);
            return;
        }

        state.AddSystemMessage($"Unknown command: {command}");
    }

    private static void RequestReadPermission(AppState state, string path)
    {
        state.ActiveModal = SelectionModalState<ReadPermissionChoice>.Create(
            new SelectionPromptRequest<ReadPermissionChoice>(
                "Allow local file read?",
                [
                    new SelectionPromptOption<ReadPermissionChoice>(
                        "Allow",
                        ReadPermissionChoice.Allow,
                        "Read the requested file from the current workspace."),
                    new SelectionPromptOption<ReadPermissionChoice>(
                        "Deny",
                        ReadPermissionChoice.Deny,
                        "Leave the file unread.")
                ],
                $"Read file '{path}'?",
                DefaultIndex: 1,
                AllowCancellation: true,
                AutoSelectAfter: TimeSpan.FromSeconds(10)),
            completionToken: new object(),
            onSelected: choice =>
            {
                if (choice == ReadPermissionChoice.Allow)
                {
                    ExecuteReadFile(state, path);
                }
                else
                {
                    state.AddSystemMessage($"Permission denied. Did not read file: {path}");
                }
            },
            onCancelled: _ => state.AddSystemMessage($"Permission denied. Did not read file: {path}"));

        state.AddSystemMessage($"Permission requested to read file: {path}");
    }

    private static void ExecuteReadFile(AppState state, string path)
    {
        try
        {
            string fullPath = GetSafePath(state, path);

            if (!File.Exists(fullPath))
            {
                state.AddSystemMessage($"File not found: {path}");
                return;
            }

            string content = File.ReadAllText(fullPath);
            string relativePath = Path.GetRelativePath(state.RootDirectory, fullPath);

            state.AddSystemMessage(
                $"""
                Permission granted.

                File: {relativePath}

                {content}
                """);
        }
        catch (Exception exception)
        {
            state.AddSystemMessage($"Error reading file: {exception.Message}");
        }
    }

    private static void ExecuteListFiles(AppState state)
    {
        try
        {
            List<string> files = Directory
                .EnumerateFiles(state.RootDirectory, "*", SearchOption.AllDirectories)
                .Where(path =>
                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Take(100)
                .Select(path => Path.GetRelativePath(state.RootDirectory, path))
                .ToList();

            if (files.Count == 0)
            {
                state.AddSystemMessage("No files found.");
                return;
            }

            state.AddSystemMessage("Files:\n\n" + string.Join('\n', files));
        }
        catch (Exception exception)
        {
            state.AddSystemMessage($"Error listing files: {exception.Message}");
        }
    }

    private static string GetSafePath(AppState state, string path)
    {
        string root = Path.GetFullPath(state.RootDirectory);
        string fullPath = Path.GetFullPath(Path.Combine(root, path));

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(root, comparison))
        {
            throw new InvalidOperationException("Path escapes workspace.");
        }

        return fullPath;
    }

    private static void UpdateStreaming(AppState state)
    {
        state.SpinnerFrame++;

        if (!state.IsStreaming)
        {
            return;
        }

        ChatMessage? message = state.GetStreamingMessage();
        if (message is null)
        {
            state.IsStreaming = false;
            state.StreamingMessageId = null;
            state.StreamQueue.Clear();
            return;
        }

        if (state.StreamQueue.Count == 0)
        {
            state.IsStreaming = false;
            state.StreamingMessageId = null;

            if (state.ClearBusyWhenStreamCompletes)
            {
                state.IsBusy = false;
                state.ClearBusyWhenStreamCompletes = false;
                state.ActivityText = state.IsReady ? "Ready" : "Idle";
            }

            return;
        }

        for (int index = 0; index < 6 && state.StreamQueue.Count > 0; index++)
        {
            message.Text += state.StreamQueue.Dequeue();
        }
    }
}
