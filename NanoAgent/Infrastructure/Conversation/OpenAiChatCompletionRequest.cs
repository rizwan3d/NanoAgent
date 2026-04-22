using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed record OpenAiChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<OpenAiChatCompletionRequestMessage> Messages,
    [property: JsonPropertyName("tools")] IReadOnlyList<OpenAiChatCompletionToolDefinition> Tools,
    [property: JsonPropertyName("reasoning_effort")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReasoningEffort = null);

internal sealed record OpenAiChatCompletionRequestMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Content,
    [property: JsonPropertyName("tool_call_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToolCallId = null,
    [property: JsonPropertyName("tool_calls")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<OpenAiChatCompletionToolCall>? ToolCalls = null);

internal sealed record OpenAiChatCompletionToolDefinition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OpenAiChatCompletionFunctionDefinition Function);

internal sealed record OpenAiChatCompletionFunctionDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] JsonElement Parameters);
