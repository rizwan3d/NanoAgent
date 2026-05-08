namespace NanoAgent.Application.Models;

public sealed class ConversationTurnResult
{
    public ConversationTurnResult(
        ConversationTurnResultKind kind,
        string responseText,
        ToolExecutionBatchResult? toolExecutionResult,
        ConversationTurnMetrics? metrics,
        string? reasoningText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseText);

        Kind = kind;
        ResponseText = responseText.Trim();
        ToolExecutionResult = toolExecutionResult;
        Metrics = metrics;
        ReasoningText = string.IsNullOrWhiteSpace(reasoningText)
            ? null
            : reasoningText.Trim();
    }

    public ConversationTurnResult(string responseText)
        : this(ConversationTurnResultKind.AssistantMessage, responseText, null, null)
    {
    }

    public ConversationTurnResult(
        ConversationTurnResultKind kind,
        string responseText)
        : this(kind, responseText, null, null)
    {
    }

    public ConversationTurnResultKind Kind { get; }

    public ConversationTurnMetrics? Metrics { get; }

    public string ResponseText { get; }

    public string? ReasoningText { get; }

    public ToolExecutionBatchResult? ToolExecutionResult { get; }

    public static ConversationTurnResult AssistantMessage(
        string responseText,
        ConversationTurnMetrics? metrics = null)
    {
        return AssistantMessage(responseText, null, metrics);
    }

    public static ConversationTurnResult AssistantMessage(
        string responseText,
        ToolExecutionBatchResult? toolExecutionResult,
        ConversationTurnMetrics? metrics = null)
    {
        return AssistantMessage(responseText, toolExecutionResult, metrics, reasoningText: null);
    }

    public static ConversationTurnResult AssistantMessage(
        string responseText,
        ToolExecutionBatchResult? toolExecutionResult,
        ConversationTurnMetrics? metrics,
        string? reasoningText)
    {
        return new ConversationTurnResult(
            ConversationTurnResultKind.AssistantMessage,
            responseText,
            toolExecutionResult,
            metrics,
            reasoningText);
    }

    public static ConversationTurnResult ToolExecution(
        string responseText,
        ConversationTurnMetrics? metrics = null)
    {
        return new ConversationTurnResult(
            ConversationTurnResultKind.ToolExecution,
            responseText,
            null,
            metrics);
    }

    public static ConversationTurnResult ToolExecution(
        ToolExecutionBatchResult toolExecutionResult,
        ConversationTurnMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(toolExecutionResult);

        return new ConversationTurnResult(
            ConversationTurnResultKind.ToolExecution,
            toolExecutionResult.ToDisplayText(),
            toolExecutionResult,
            metrics);
    }
}
