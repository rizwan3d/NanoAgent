using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Tools.Services;

internal sealed class ToolExecutionPipeline : IStreamingToolExecutionPipeline
{
    private readonly IToolAuditLogService? _toolAuditLogService;
    private readonly ILessonMemoryService? _lessonMemoryService;
    private readonly TimeProvider _timeProvider;
    private readonly IToolInvoker _toolInvoker;

    public ToolExecutionPipeline(
        IToolInvoker toolInvoker,
        ILessonMemoryService? lessonMemoryService = null,
        IToolAuditLogService? toolAuditLogService = null,
        TimeProvider? timeProvider = null)
    {
        _toolInvoker = toolInvoker;
        _lessonMemoryService = lessonMemoryService;
        _toolAuditLogService = toolAuditLogService;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

            DateTimeOffset startedAtUtc = _timeProvider.GetUtcNow();
            ToolInvocationResult result = await _toolInvoker.InvokeAsync(
                toolCall,
                session,
                executionPhase,
                allowedToolNames,
                cancellationToken);
            DateTimeOffset completedAtUtc = _timeProvider.GetUtcNow();

            await ObserveLessonMemoryAsync(toolCall, result, cancellationToken);
            await RecordToolAuditAsync(
                toolCall,
                result,
                session,
                executionPhase,
                startedAtUtc,
                completedAtUtc,
                cancellationToken);
            results.Add(result);
            if (onToolResult is not null)
            {
                await onToolResult(result, cancellationToken);
            }
        }

        return new ToolExecutionBatchResult(results);
    }

    private async Task ObserveLessonMemoryAsync(
        ConversationToolCall toolCall,
        ToolInvocationResult result,
        CancellationToken cancellationToken)
    {
        if (_lessonMemoryService is null)
        {
            return;
        }

        try
        {
            await _lessonMemoryService.ObserveToolResultAsync(toolCall, result, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Lesson memory is helpful context, but tool execution should not fail because
            // the local memory file is temporarily unavailable or malformed.
        }
    }

    private async Task RecordToolAuditAsync(
        ConversationToolCall toolCall,
        ToolInvocationResult result,
        ReplSessionContext session,
        ConversationExecutionPhase executionPhase,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        if (_toolAuditLogService is null)
        {
            return;
        }

        try
        {
            await _toolAuditLogService.RecordAsync(
                toolCall,
                result,
                session,
                executionPhase,
                startedAtUtc,
                completedAtUtc,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Audit logs are useful operational evidence, but a log write issue should
            // not turn a completed tool call into a failed agent turn.
        }
    }
}
