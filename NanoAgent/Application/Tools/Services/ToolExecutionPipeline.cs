using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Tools.Services;

internal sealed class ToolExecutionPipeline : IStreamingToolExecutionPipeline
{
    private readonly IToolInvoker _toolInvoker;

    public ToolExecutionPipeline(IToolInvoker toolInvoker)
    {
        _toolInvoker = toolInvoker;
    }

    public Task<ToolExecutionBatchResult> ExecuteAsync(
        IReadOnlyList<ConversationToolCall> toolCalls,
        ReplSessionContext session,
        ConversationExecutionPhase executionPhase,
        IReadOnlySet<string> allowedToolNames,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            toolCalls,
            session,
            executionPhase,
            allowedToolNames,
            cancellationToken,
            onToolResult: null);
    }

    public async Task<ToolExecutionBatchResult> ExecuteAsync(
        IReadOnlyList<ConversationToolCall> toolCalls,
        ReplSessionContext session,
        ConversationExecutionPhase executionPhase,
        IReadOnlySet<string> allowedToolNames,
        CancellationToken cancellationToken,
        Func<ToolInvocationResult, CancellationToken, Task>? onToolResult)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(allowedToolNames);
        cancellationToken.ThrowIfCancellationRequested();

        if (toolCalls.Count == 0)
        {
            return new ToolExecutionBatchResult([]);
        }

        List<ToolInvocationResult> results = new(toolCalls.Count);
        using IDisposable _ = session.BeginFileEditTransactionBatch();

        foreach (ConversationToolCall toolCall in toolCalls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ToolInvocationResult result = await _toolInvoker.InvokeAsync(
                toolCall,
                session,
                executionPhase,
                allowedToolNames,
                cancellationToken);

            results.Add(result);
            if (onToolResult is not null)
            {
                await onToolResult(result, cancellationToken);
            }
        }

        return new ToolExecutionBatchResult(results);
    }
}
