using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;

namespace NanoAgent.CLI;

public sealed class UiBridge
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action<AppState>> _pending = new();

    public void ApplyPending(AppState state)
    {
        while (_pending.TryDequeue(out Action<AppState>? update))
        {
            update(state);
        }
    }

    public void Enqueue(Action<AppState> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        _pending.Enqueue(update);
    }

    public Task<T> RequestSelectionAsync<T>(
        SelectionPromptRequest<T> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        object completionToken = new();

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                completion.TrySetCanceled(cancellationToken);
                Enqueue(state =>
                {
                    if (ReferenceEquals(state.ActiveModal?.CompletionToken, completionToken))
                    {
                        state.ActiveModal = null;
                    }
                });
            });
        }

        Enqueue(state =>
        {
            state.ActiveModal = SelectionModalState<T>.Create(
                request,
                completionToken,
                onSelected: value => completion.TrySetResult(value),
                onCancelled: exception => completion.TrySetException(exception));
        });

        return completion.Task;
    }

    public Task<string> RequestTextAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        object completionToken = new();

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                completion.TrySetCanceled(cancellationToken);
                Enqueue(state =>
                {
                    if (ReferenceEquals(state.ActiveModal?.CompletionToken, completionToken))
                    {
                        state.ActiveModal = null;
                    }
                });
            });
        }

        Enqueue(state =>
        {
            state.ActiveModal = TextModalState.Create(
                request,
                isSecret,
                completionToken,
                onSubmitted: value => completion.TrySetResult(value),
                onCancelled: exception => completion.TrySetException(exception));
        });

        return completion.Task;
    }

    public void ShowError(string message)
    {
        Enqueue(state => state.AddSystemMessage($"Error: {message}"));
    }

    public void ShowInfo(string message)
    {
        Enqueue(state => state.AddSystemMessage(message));
    }

    public void ShowSuccess(string message)
    {
        Enqueue(state => state.AddSystemMessage($"Success: {message}"));
    }

    public void ShowToolCalls(IReadOnlyList<ConversationToolCall> toolCalls)
    {
        string names = string.Join(
            ", ",
            toolCalls.Select(static toolCall => toolCall.Name));

        Enqueue(state =>
        {
            state.ActivityText = string.IsNullOrWhiteSpace(names)
                ? "Running tools"
                : $"Running tools: {names}";

            if (!string.IsNullOrWhiteSpace(names))
            {
                state.AddSystemMessage($"Running tools: {names}");
            }
        });
    }

    public void ShowToolResults(ToolExecutionBatchResult toolExecutionResult)
    {
        Enqueue(state =>
        {
            foreach (ToolInvocationResult result in toolExecutionResult.Results)
            {
                string prefix = result.Result.IsSuccess
                    ? "Tool complete"
                    : "Tool issue";

                state.AddSystemMessage($"{prefix}: {result.ToolName}. {result.Result.Message}");
            }
        });
    }

    public void ShowExecutionPlan(ExecutionPlanProgress progress)
    {
        string description = BuildPlanDescription(progress);

        Enqueue(state =>
        {
            state.ActivityText = progress.Tasks.Count == 0
                ? "Working"
                : $"Plan {progress.CompletedTaskCount}/{progress.Tasks.Count}";

            state.AddSystemMessage(description);
        });
    }

    private static string BuildPlanDescription(ExecutionPlanProgress progress)
    {
        if (progress.Tasks.Count == 0)
        {
            return "Plan updated.";
        }

        List<string> lines =
        [
            $"Plan progress: {progress.CompletedTaskCount}/{progress.Tasks.Count}"
        ];

        for (int index = 0; index < progress.Tasks.Count; index++)
        {
            string marker = index < progress.CompletedTaskCount
                ? "[x]"
                : index == progress.CurrentTaskIndex
                    ? "[>]"
                    : "[ ]";

            lines.Add($"{marker} {progress.Tasks[index]}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
