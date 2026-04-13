using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoAgent;

internal sealed class ToolExecutionResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "success";

    [JsonPropertyName("tool")]
    public string Tool { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    [JsonPropertyName("entries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Entries { get; set; }

    [JsonPropertyName("pattern")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pattern { get; set; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }

    [JsonPropertyName("matches")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Matches { get; set; }

    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Truncated { get; set; }

    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; set; }

    [JsonPropertyName("shell")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Shell { get; set; }

    [JsonPropertyName("executed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Executed { get; set; }

    [JsonPropertyName("workdir")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Workdir { get; set; }

    [JsonPropertyName("exit_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExitCode { get; set; }

    [JsonPropertyName("stdout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stdout { get; set; }

    [JsonPropertyName("stderr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stderr { get; set; }

    [JsonPropertyName("diff")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Diff { get; set; }

    [JsonPropertyName("changes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Changes { get; set; }
}

internal static class ToolExecutionResults
{
    public static string Success(string tool, Action<ToolExecutionResult>? configure = null)
    {
        ToolExecutionResult result = new()
        {
            Tool = tool,
            Status = "success"
        };

        configure?.Invoke(result);
        return JsonSerializer.Serialize(result, ToolResultJsonContext.Default.ToolExecutionResult);
    }

    public static string Error(string tool, string message, Action<ToolExecutionResult>? configure = null)
    {
        ToolExecutionResult result = new()
        {
            Tool = tool,
            Status = "error",
            Message = message
        };

        configure?.Invoke(result);
        return JsonSerializer.Serialize(result, ToolResultJsonContext.Default.ToolExecutionResult);
    }

    public static bool TryParse(string json, out ToolExecutionResult? result)
    {
        try
        {
            result = JsonSerializer.Deserialize(json, ToolResultJsonContext.Default.ToolExecutionResult);
            return result is not null;
        }
        catch (JsonException)
        {
            result = null;
            return false;
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ToolExecutionResult))]
internal sealed partial class ToolResultJsonContext : JsonSerializerContext
{
}
