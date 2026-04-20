using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Tools.Services;

internal sealed class ToolExecutionPipeline : IToolExecutionPipeline
{
    private readonly IToolInvoker _toolInvoker;

    public ToolExecutionPipeline(IToolInvoker toolInvoker)
    {
        _toolInvoker = toolInvoker;
    }

    public async Task<ToolExecutionBatchResult> ExecuteAsync(
        IReadOnlyList<ConversationToolCall> toolCalls,
        ReplSessionContext session,
        ConversationExecutionPhase executionPhase,
        IReadOnlySet<string> allowedToolNames,
        CancellationToken cancellationToken)
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
        }

        return new ToolExecutionBatchResult(results);
    }
}
