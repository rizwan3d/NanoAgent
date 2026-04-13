using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NanoAgent;

internal static class ToolArgumentParser
{
    public static TArguments? Parse<TArguments>(
        ChatToolCall toolCall,
        string toolName,
        JsonTypeInfo<TArguments> typeInfo,
        out string? errorMessage)
        where TArguments : class
    {
        try
        {
            errorMessage = null;
            return JsonSerializer.Deserialize(toolCall.Function.Arguments, typeInfo);
        }
        catch (JsonException exception)
        {
            errorMessage = ToolExecutionResults.Error(toolName, $"Invalid arguments. {exception.Message}");
            return null;
        }
    }
}
