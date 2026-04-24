using NanoAgent.Application.Exceptions;
using System.Text.Json;
using NanoAgent.Application.Models;

namespace NanoAgent.CLI;

public sealed class UiBridge
{
    private const int MaxActivityDescriptionLength = 96;

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
        string[] descriptions = toolCalls
            .Select(DescribeToolCall)
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
        Enqueue(state =>
        {
            foreach (ToolInvocationResult result in toolExecutionResult.Results)
            {
                if (IsSuccessfulPlanUpdate(result))
                {
                    continue;
                }

                state.AddSystemMessage(BuildToolResultMessage(result));
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

    private static string DescribeToolCall(ConversationToolCall toolCall)
    {
        string name = toolCall.Name.Trim();

        return name switch
        {
            "shell_command" when TryGetArgumentString(toolCall.ArgumentsJson, "command", out string? command) =>
                $"command: {Truncate(command, 120)}",
            "file_read" when TryGetArgumentString(toolCall.ArgumentsJson, "path", out string? path) =>
                $"file read: {path}",
            "directory_list" when TryGetArgumentString(toolCall.ArgumentsJson, "path", out string? path) =>
                $"directory list: {path}",
            "directory_list" => "directory list",
            "search_files" when TryGetArgumentString(toolCall.ArgumentsJson, "query", out string? query) =>
                $"file search: \"{query}\"",
            "text_search" when TryGetArgumentString(toolCall.ArgumentsJson, "query", out string? query) =>
                $"text search: \"{query}\"",
            "file_write" when TryGetArgumentString(toolCall.ArgumentsJson, "path", out string? path) =>
                $"file write: {path}",
            "web_run" => DescribeWebRunCall(toolCall.ArgumentsJson),
            _ => name
        };
    }

    private static string DescribeWebRunCall(string argumentsJson)
    {
        if (TryGetFirstArrayObjectString(argumentsJson, "search_query", "q", out string? query))
        {
            return $"web search: \"{query}\"";
        }

        if (TryGetFirstArrayObjectString(argumentsJson, "open", "ref_id", out string? refId))
        {
            return $"web open: {refId}";
        }

        if (TryGetFirstArrayObjectString(argumentsJson, "find", "pattern", out string? pattern))
        {
            return $"web find: \"{pattern}\"";
        }

        return "web_run";
    }

    private static string BuildToolResultMessage(ToolInvocationResult invocationResult)
    {
        ToolRenderPayload? renderPayload = invocationResult.Result.RenderPayload;
        if (renderPayload is not null)
        {
            string prefix = invocationResult.Result.IsSuccess
                ? string.Empty
                : "Tool issue: ";

            return $"{prefix}{renderPayload.Title}{Environment.NewLine}{Environment.NewLine}{renderPayload.Text}";
        }

        string title = invocationResult.Result.IsSuccess
            ? $"Tool complete: {invocationResult.ToolName}"
            : $"Tool issue: {invocationResult.ToolName}";

        return $"{title}{Environment.NewLine}{Environment.NewLine}{invocationResult.Result.Message}";
    }

    private static bool IsSuccessfulPlanUpdate(ToolInvocationResult invocationResult)
    {
        return invocationResult.Result.IsSuccess &&
            string.Equals(invocationResult.ToolName, "update_plan", StringComparison.Ordinal);
    }

    private static bool TryGetArgumentString(
        string argumentsJson,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.GetString()?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetFirstArrayObjectString(
        string argumentsJson,
        string arrayPropertyName,
        string itemPropertyName,
        out string value)
    {
        value = string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(arrayPropertyName, out JsonElement array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty(itemPropertyName, out JsonElement property) ||
                    property.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                value = property.GetString()?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
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
}
