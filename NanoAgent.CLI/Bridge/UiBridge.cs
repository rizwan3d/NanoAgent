using System.Collections.Concurrent;
using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;
using NanoAgent.Application.UI;

namespace NanoAgent.CLI;

public sealed class UiBridge : IUiBridge
{
    private const int MaxActivityDescriptionLength = 96;

    private readonly ConcurrentQueue<Action<AppState>> _pending = new();
    private readonly IPlanOutputFormatter _planOutputFormatter;
    private readonly IToolOutputFormatter _toolOutputFormatter;
    private readonly object _providerAuthKeySync = new();
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
        cancellationToken.ThrowIfCancellationRequested();

        if (TryConsumeProviderAuthKey(request, isSecret, out string providerAuthKey))
        {
            return Task.FromResult(providerAuthKey);
        }

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
        string[] descriptions = toolCalls
            .Select(_toolOutputFormatter.DescribeCall)
            .Where(static description => !string.IsNullOrWhiteSpace(description))
            .ToArray();

        Enqueue(state =>
        {
            state.ActivityText = descriptions.Length == 0
                ? "Running tools"
                : $"Running {Truncate(descriptions[0], MaxActivityDescriptionLength)}";

            if (descriptions.Length == 1)
            {
                state.AddSystemMessage($"Running {descriptions[0]}");
            }
            else if (descriptions.Length > 1)
            {
                state.AddSystemMessage(
                    "Running tools:" +
                    Environment.NewLine +
                    string.Join(
                        Environment.NewLine,
                        descriptions.Select(static description => $"- {description}")));
            }
        });
    }

    public void ShowToolResults(ToolExecutionBatchResult toolExecutionResult)
    {
        IReadOnlyList<string> messages = _toolOutputFormatter.FormatResults(toolExecutionResult);

        Enqueue(state =>
        {
            foreach (string message in messages)
            {
                state.AddSystemMessage(message);
            }
        });
    }

    public void ShowExecutionPlan(ExecutionPlanProgress progress)
    {
        string description = _planOutputFormatter.Format(progress);

        Enqueue(state =>
        {
            state.ActivityText = progress.Tasks.Count == 0
                ? "Working"
                : $"Plan {progress.CompletedTaskCount}/{progress.Tasks.Count}";
            state.LatestPlanText = description;

            state.AddSystemMessage(description);
        });
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
}
