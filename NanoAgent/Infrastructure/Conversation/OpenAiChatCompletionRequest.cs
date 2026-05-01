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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Content,
    [property: JsonPropertyName("tool_call_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToolCallId = null,
    [property: JsonPropertyName("tool_calls")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<OpenAiChatCompletionToolCall>? ToolCalls = null);

internal sealed record OpenAiChatCompletionContentPart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonPropertyName("image_url")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OpenAiChatCompletionImageUrl? ImageUrl = null);

internal sealed record OpenAiChatCompletionImageUrl(
    [property: JsonPropertyName("url")] string Url);

internal sealed record OpenAiChatCompletionToolDefinition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OpenAiChatCompletionFunctionDefinition Function);

internal sealed record OpenAiChatCompletionFunctionDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] JsonElement Parameters);
