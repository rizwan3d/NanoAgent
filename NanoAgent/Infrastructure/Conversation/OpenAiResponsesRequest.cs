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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OpenAiResponsesReasoning? Reasoning,
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

internal sealed record OpenAiResponsesReasoning(
    [property: JsonPropertyName("effort")] string Effort,
    [property: JsonPropertyName("summary")] string Summary);

