using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed record OpenAiChatCompletionResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChatCompletionChoice>? Choices,
    [property: JsonPropertyName("usage")] OpenAiChatCompletionUsage? Usage);

internal sealed record OpenAiChatCompletionChoice(
    [property: JsonPropertyName("message")] OpenAiChatCompletionResponseMessage? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

internal sealed record OpenAiChatCompletionResponseMessage(
    [property: JsonPropertyName("content")] JsonElement Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiChatCompletionToolCall>? ToolCalls,
    [property: JsonPropertyName("function_call")] OpenAiChatCompletionFunctionCall? FunctionCall,
    [property: JsonPropertyName("refusal")] string? Refusal);

internal sealed record OpenAiChatCompletionToolCall(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("function")] OpenAiChatCompletionFunctionCall? Function);

internal sealed record OpenAiChatCompletionFunctionCall(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("arguments")] string? Arguments);

internal sealed record OpenAiChatCompletionUsage(
    [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
    [property: JsonPropertyName("total_tokens")] int? TotalTokens,
    [property: JsonPropertyName("prompt_tokens_details")] OpenAiChatCompletionUsageDetails? PromptTokensDetails);

internal sealed record OpenAiChatCompletionUsageDetails(
    [property: JsonPropertyName("cached_tokens")] int? CachedTokens);
