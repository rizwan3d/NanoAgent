using NanoAgent.Application.Models;

namespace NanoAgent.CLI;

public interface IUiBridge
{
    void ApplyPending(AppState state);

    void Enqueue(Action<AppState> update);

    Task<T> RequestSelectionAsync<T>(
        SelectionPromptRequest<T> request,
        CancellationToken cancellationToken);

    Task<string> RequestTextAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken);

    void ShowError(string message);

    void ShowInfo(string message);

    void ShowSuccess(string message);

    void ShowToolCalls(IReadOnlyList<ConversationToolCall> toolCalls);

    void ShowToolResults(ToolExecutionBatchResult toolExecutionResult);

    void ShowExecutionPlan(ExecutionPlanProgress progress);
}
