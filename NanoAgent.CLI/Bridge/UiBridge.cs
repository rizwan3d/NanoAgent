using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;
using NanoAgent.Application.UI;
using System.Collections.Concurrent;

namespace NanoAgent.CLI;

public sealed class UiBridge : IUiBridge
{
    private const int MaxActivityDescriptionLength = 96;

    private readonly ConcurrentQueue<Action<AppState>> _pending = new();
    private readonly IPlanOutputFormatter _planOutputFormatter;
    private readonly IToolOutputFormatter _toolOutputFormatter;
    private readonly object _providerAuthKeySync = new();
    private int _assistantMessageChunkCount;
    private long _activeCliOperationId;
    private string? _providerAuthKey;
    private bool _providerAuthKeyConsumed;

    public UiBridge(string? providerAuthKey = null)
        : this(new ToolOutputFormatter(), new PlanOutputFormatter(), providerAuthKey)
    {
    }

    internal UiBridge(
        IToolOutputFormatter toolOutputFormatter,
        IPlanOutputFormatter planOutputFormatter,
        string? providerAuthKey = null)
    {
        _toolOutputFormatter = toolOutputFormatter ?? throw new ArgumentNullException(nameof(toolOutputFormatter));
        _planOutputFormatter = planOutputFormatter ?? throw new ArgumentNullException(nameof(planOutputFormatter));
        _providerAuthKey = NormalizeOrNull(providerAuthKey);
    }

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

    internal void SetActiveCliOperation(long operationId)
    {
        Interlocked.Exchange(ref _activeCliOperationId, operationId);
    }

    public async Task<T> RequestSelectionAsync<T>(
        SelectionPromptRequest<T> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        object completionToken = new();

        using CancellationTokenRegistration registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(() =>
            {
                completion.TrySetCanceled(cancellationToken);
                Enqueue(state =>
                {
                    if (ReferenceEquals(state.ActiveModal?.CompletionToken, completionToken))
                    {
                        state.ActiveModal = null;
                    }
                });
            })
            : default;

        Enqueue(state =>
        {
            state.ActiveModal = SelectionModalState<T>.Create(
                request,
                completionToken,
                onSelected: value => completion.TrySetResult(value),
                onCancelled: exception => completion.TrySetException(exception));
        });

        return await completion.Task.ConfigureAwait(false);
    }

    public async Task<string> RequestTextAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (TryConsumeProviderAuthKey(request, isSecret, out string providerAuthKey))
        {
            return providerAuthKey;
        }

        TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        object completionToken = new();

        using CancellationTokenRegistration registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(() =>
            {
                completion.TrySetCanceled(cancellationToken);
                Enqueue(state =>
                {
                    if (ReferenceEquals(state.ActiveModal?.CompletionToken, completionToken))
                    {
                        state.ActiveModal = null;
                    }
                });
            })
            : default;

        Enqueue(state =>
        {
            state.ActiveModal = TextModalState.Create(
                request,
                isSecret,
                completionToken,
                onSubmitted: value => completion.TrySetResult(value),
                onCancelled: exception => completion.TrySetException(exception));
        });

        return await completion.Task.ConfigureAwait(false);
    }

    public void ShowError(string message)
    {
        EnqueueForActiveOperation(state => state.AddSystemMessage($"Error: {message}"));
    }

    public void ShowInfo(string message)
    {
        EnqueueForActiveOperation(state => state.AddSystemMessage(message));
    }

    public void ShowSuccess(string message)
    {
        EnqueueForActiveOperation(state => state.AddSystemMessage($"Success: {message}"));
    }

    public void ShowAssistantMessageChunk(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Interlocked.Increment(ref _assistantMessageChunkCount);

        EnqueueForActiveOperation(state =>
        {
            state.ActivityText = "Streaming response";
            state.ClearBusyWhenStreamCompletes = true;
            state.AppendAssistantStreamChunk(text);
        });
    }

    public void ShowAssistantReasoning(string reasoningText)
    {
        if (string.IsNullOrWhiteSpace(reasoningText))
        {
            return;
        }

        EnqueueForActiveOperation(state =>
        {
            state.ActivityText = "Thinking";
            state.AddThinkingMessage("Thinking:\n\n" + reasoningText.Trim());
        });
    }

    public void ShowToolCalls(IReadOnlyList<ConversationToolCall> toolCalls)
    {
        string[] descriptions = toolCalls
            .Select(_toolOutputFormatter.DescribeCall)
            .Where(static description => !string.IsNullOrWhiteSpace(description))
            .ToArray();

        EnqueueForActiveOperation(state =>
        {
            state.ActivityText = descriptions.Length == 0
                ? "Running tools"
                : $"Running {Truncate(descriptions[0], MaxActivityDescriptionLength)}";

            if (descriptions.Length == 1)
            {
                ChatMessage msg = state.AddSystemMessage($"Running {descriptions[0]}", isCollapsibleToolMessage: true);
                msg.IsToolCallMessage = true;
            }
            else if (descriptions.Length > 1)
            {
                ChatMessage msg = state.AddSystemMessage(
                    "Running tools:" +
                    Environment.NewLine +
                    string.Join(
                        Environment.NewLine,
                        descriptions.Select(static description => $"- {description}")),
                    isCollapsibleToolMessage: true);
                msg.IsToolCallMessage = true;
            }
        });
    }

    public void ShowToolResults(ToolExecutionBatchResult toolExecutionResult)
    {
        IReadOnlyList<string> messages;
    
        try
        {
            messages = _toolOutputFormatter.FormatResults(toolExecutionResult);
        }
        catch (Exception ex)
        {
            messages =
            [
                $"Tool result display failed: {ex.Message}"
            ];
        }
    
        EnqueueForActiveOperation(state =>
        {
            // Merge the preceding tool-call message (if any) into the first result message
            // so the tool call notification and its output appear as one collapsed block.
            ChatMessage? toolCallMsg = null;
            for (int index = state.Messages.Count - 1; index >= 0; index--)
            {
                if (state.Messages[index].IsToolCallMessage)
                {
                    toolCallMsg = state.Messages[index];
                    state.Messages.RemoveAt(index);
                    state.ExpandedToolMessageIds.Remove(toolCallMsg.Id);
                    break;
                }
            }

            bool isFirstResult = true;
            foreach (string message in messages)
            {
                ChatMessage resultMsg = state.AddSystemMessage(message, isCollapsibleToolMessage: true);

                // Prepend the tool call text to the first result message so both appear
                // together in the same collapsed block when the view is collapsed.
                if (isFirstResult && toolCallMsg is not null)
                {
                    resultMsg.Text = toolCallMsg.Text + Environment.NewLine + Environment.NewLine + resultMsg.Text;
                    isFirstResult = false;
                }
            }
        });
    }

    public void ShowExecutionPlan(ExecutionPlanProgress progress)
    {
        string description = _planOutputFormatter.Format(progress);

        EnqueueForActiveOperation(state =>
        {
            state.ActivityText = progress.Tasks.Count == 0
                ? "Working"
                : $"Plan {progress.CompletedTaskCount}/{progress.Tasks.Count}";
            state.LatestPlanProgress = progress;
            state.LatestPlanText = description;

            if (!state.IsPlanPinned)
            {
                state.PlanScrollOffset = 0;
            }

            state.IsPlanPinned = true;

            state.AddSystemMessage(description, isCollapsibleToolMessage: true);
        });
    }

    public void ShowProviderRetry(ProviderRetryProgress progress)
    {
        EnqueueForActiveOperation(state =>
        {
            state.ActivityText = $"Trying {progress.Attempt}/{progress.MaxAttempts}";
            state.AddSystemMessage(
                $"Provider unreachable ({progress.Reason}). Retrying… ({progress.Attempt}/{progress.MaxAttempts})",
                isCollapsibleToolMessage: true);
        });
    }

    internal bool HasObservedAssistantMessageChunks()
    {
        return Volatile.Read(ref _assistantMessageChunkCount) > 0;
    }

    internal void ResetAssistantMessageChunkTracking()
    {
        Interlocked.Exchange(ref _assistantMessageChunkCount, 0);
    }

    private static string Truncate(string value, int maxLength)
    {
        string normalized = value.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private bool TryConsumeProviderAuthKey(
        TextPromptRequest request,
        bool isSecret,
        out string providerAuthKey)
    {
        providerAuthKey = string.Empty;
        if (!isSecret || !IsProviderAuthKeyPrompt(request))
        {
            return false;
        }

        lock (_providerAuthKeySync)
        {
            if (_providerAuthKeyConsumed || string.IsNullOrWhiteSpace(_providerAuthKey))
            {
                return false;
            }

            providerAuthKey = _providerAuthKey;
            _providerAuthKeyConsumed = true;
            _providerAuthKey = null;
            return true;
        }
    }

    private static bool IsProviderAuthKeyPrompt(TextPromptRequest request)
    {
        string label = request.Label.Trim();
        return string.Equals(label, "API key", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Provider auth key", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private void EnqueueForActiveOperation(Action<AppState> update)
    {
        long operationId = Volatile.Read(ref _activeCliOperationId);

        Enqueue(state =>
        {
            if (operationId != 0 && !state.IsTrackedOperationCurrent(operationId))
            {
                return;
            }

            update(state);
        });
    }
}
