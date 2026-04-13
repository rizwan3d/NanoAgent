using System.Text.Json.Serialization;

namespace NanoAgent;

internal static class ChatRole
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}

internal sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("tools")]
    public ChatToolDefinition[] Tools { get; set; } = Array.Empty<ChatToolDefinition>();

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Stream { get; set; }

    [JsonPropertyName("stream_options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChatStreamOptions? StreamOptions { get; set; }
}

internal sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public ChatChoice[] Choices { get; set; } = Array.Empty<ChatChoice>();

    [JsonPropertyName("usage")]
    public ChatUsage? Usage { get; set; }
}

internal sealed class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    [JsonPropertyName("delta")]
    public ChatMessageDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("tool_calls")]
    public ChatToolCall[]? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }
}

internal sealed class ChatMessageDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public ChatToolCallDelta[]? ToolCalls { get; set; }
}

internal sealed class ChatToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public ChatToolFunctionDefinition Function { get; set; } = new();
}

internal sealed class ChatToolFunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public ChatToolParameters Parameters { get; set; } = new();
}

internal sealed class ChatToolParameters
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, ChatToolParameterProperty> Properties { get; set; } = [];

    [JsonPropertyName("required")]
    public string[] Required { get; set; } = Array.Empty<string>();

    [JsonPropertyName("additionalProperties")]
    public bool AdditionalProperties { get; set; }
}

internal sealed class ChatToolParameterProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Enum { get; set; }

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChatToolParameterProperty? Items { get; set; }

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ChatToolParameterProperty>? Properties { get; set; }

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Required { get; set; }

    [JsonPropertyName("additionalProperties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AdditionalProperties { get; set; }

    [JsonPropertyName("minLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinLength { get; set; }

    [JsonPropertyName("maxLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; set; }

    [JsonPropertyName("minimum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Minimum { get; set; }

    [JsonPropertyName("maximum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Maximum { get; set; }
}

internal sealed class ChatToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public ChatToolFunctionCall Function { get; set; } = new();
}

internal sealed class ChatToolCallDelta
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public ChatToolFunctionCallDelta? Function { get; set; }
}

internal sealed class ChatToolFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

internal sealed class ChatToolFunctionCallDelta
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

internal sealed class ChatStreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool IncludeUsage { get; set; }
}

internal sealed class ChatUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

internal sealed class ReadFileToolArguments
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

internal sealed class ListFilesToolArguments
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

internal sealed class RunCommandToolArguments
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;
}

internal sealed class WriteFileToolArguments
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

internal sealed class EditFileToolArguments
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("old_text")]
    public string OldText { get; set; } = string.Empty;

    [JsonPropertyName("new_text")]
    public string NewText { get; set; } = string.Empty;

    [JsonPropertyName("replace_all")]
    public bool ReplaceAll { get; set; }
}

internal sealed class CodeSearchToolArguments
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

internal sealed class ApplyPatchToolArguments
{
    [JsonPropertyName("patch")]
    public string Patch { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(ChatUsage))]
internal partial class NanoAgentJsonContext : JsonSerializerContext
{
}
