using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed record OpenAiResponsesRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] IReadOnlyList<OpenAiResponsesInputItem> Input,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("store")] bool Store,
    [property: JsonPropertyName("instructions")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instructions,
    [property: JsonPropertyName("tools")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<OpenAiResponsesToolDefinition>? Tools,
    [property: JsonPropertyName("reasoning")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProviderReasoningConfig? Reasoning,
    [property: JsonPropertyName("include")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Include,
    [property: JsonPropertyName("parallel_tool_calls")] bool ParallelToolCalls);

internal sealed record OpenAiResponsesInputItem(
    [property: JsonPropertyName("type")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Type = null,
    [property: JsonPropertyName("role")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Role = null,
    [property: JsonPropertyName("content")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<OpenAiResponsesContentPart>? Content = null,
    [property: JsonPropertyName("call_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CallId = null,
    [property: JsonPropertyName("name")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonPropertyName("arguments")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Arguments = null,
    [property: JsonPropertyName("output")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Output = null);

internal sealed record OpenAiResponsesContentPart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonPropertyName("image_url")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ImageUrl = null);

internal sealed record OpenAiResponsesToolDefinition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] JsonElement Parameters,
    [property: JsonPropertyName("strict")] bool Strict);

internal sealed record ProviderReasoningConfig(
    [property: JsonPropertyName("effort")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Effort = null,
    [property: JsonPropertyName("summary")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Summary = null,
    [property: JsonPropertyName("max_tokens")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxTokens = null,
    [property: JsonPropertyName("exclude")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Exclude = null);

internal sealed record GeminiThinkingConfig(
    [property: JsonPropertyName("thinkingLevel")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ThinkingLevel = null,
    [property: JsonPropertyName("includeThoughts")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IncludeThoughts = null,
    [property: JsonPropertyName("thinkingBudget")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ThinkingBudget = null);

