namespace NanoAgent.Application.Models;

public sealed record ConversationResponse(
    string? AssistantMessage,
    IReadOnlyList<ConversationToolCall> ToolCalls,
    string? ResponseId,
    int? CompletionTokens = null,
    int? PromptTokens = null,
    int? TotalTokens = null,
    int? CachedPromptTokens = null)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
}
