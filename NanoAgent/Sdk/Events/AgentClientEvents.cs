using NanoAgent.Application.Models;

namespace NanoAgent.Sdk.Events;

/// <summary>Severity of a status message surfaced by the agent.</summary>
public enum StatusMessageSeverity
{
    Info = 0,
    Success = 1,
    Error = 2
}

/// <summary>Raised when the agent emits intermediate reasoning text.</summary>
public sealed class AssistantReasoningEventArgs : EventArgs
{
    public AssistantReasoningEventArgs(string reasoningText)
    {
        ReasoningText = reasoningText ?? string.Empty;
    }

    public string ReasoningText { get; }
}

/// <summary>Raised when the agent emits incremental assistant text.</summary>
public sealed class AssistantMessageChunkEventArgs : EventArgs
{
    public AssistantMessageChunkEventArgs(string text)
    {
        Text = text ?? string.Empty;
    }

    public string Text { get; }
}

/// <summary>Raised when the agent is about to execute a batch of tool calls.</summary>
public sealed class ToolCallsEventArgs : EventArgs
{
    public ToolCallsEventArgs(IReadOnlyList<ConversationToolCall> toolCalls)
    {
        ToolCalls = toolCalls ?? [];
    }

    public IReadOnlyList<ConversationToolCall> ToolCalls { get; }
}

/// <summary>Raised when a batch of tool calls finishes and results are available.</summary>
public sealed class ToolResultsEventArgs : EventArgs
{
    public ToolResultsEventArgs(ToolExecutionBatchResult results)
    {
        Results = results;
    }

    public ToolExecutionBatchResult Results { get; }
}

/// <summary>Raised when the agent's execution plan changes during planning mode.</summary>
public sealed class ExecutionPlanEventArgs : EventArgs
{
    public ExecutionPlanEventArgs(ExecutionPlanProgress progress)
    {
        Progress = progress;
    }

    public ExecutionPlanProgress Progress { get; }
}

/// <summary>Raised when a provider request is being retried (for example a transient network error).</summary>
public sealed class ProviderRetryEventArgs : EventArgs
{
    public ProviderRetryEventArgs(ProviderRetryProgress progress)
    {
        Progress = progress;
    }

    public ProviderRetryProgress Progress { get; }
}

/// <summary>Raised when the agent surfaces an informational, success, or error status message.</summary>
public sealed class StatusMessageEventArgs : EventArgs
{
    public StatusMessageEventArgs(StatusMessageSeverity severity, string message)
    {
        Severity = severity;
        Message = message ?? string.Empty;
    }

    public StatusMessageSeverity Severity { get; }

    public string Message { get; }
}
