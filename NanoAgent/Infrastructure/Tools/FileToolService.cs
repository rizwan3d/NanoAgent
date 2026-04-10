using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoAgent;

internal sealed class FileToolService
{
    public ChatToolDefinition[] GetToolDefinitions() =>
    [
        new()
        {
            Function = new ChatToolFunctionDefinition
            {
                Name = "read_file",
                Description = "Read a UTF-8 text file from disk. Use this to inspect project files before answering code questions.",
                Parameters = new ChatToolParameters
                {
                    AdditionalProperties = false,
                    Required = ["path"],
                    Properties = new Dictionary<string, ChatToolParameterProperty>
                    {
                        ["path"] = new()
                        {
                            Type = "string",
                            Description = "Relative or absolute path to a text file."
                        }
                    }
                }
            }
        }
    ];

    public string Execute(ChatToolCall toolCall)
    {
        if (!string.Equals(toolCall.Function.Name, "read_file", StringComparison.Ordinal))
        {
            return $"Tool error: unsupported tool '{toolCall.Function.Name}'.";
        }

        ReadFileToolArguments? arguments;
        try
        {
            arguments = JsonSerializer.Deserialize(
                toolCall.Function.Arguments,
                FileToolJsonContext.Default.ReadFileToolArguments);
        }
        catch (JsonException exception)
        {
            return $"Tool error: invalid arguments for read_file. {exception.Message}";
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Path))
        {
            return "Tool error: 'path' is required.";
        }

        string fullPath = ResolvePath(arguments.Path);
        if (!File.Exists(fullPath))
        {
            return $"Tool error: file not found: {fullPath}";
        }

        try
        {
            string content = File.ReadAllText(fullPath);
            return $"FILE: {fullPath}\n{content}";
        }
        catch (Exception exception)
        {
            return $"Tool error: unable to read file '{fullPath}'. {exception.Message}";
        }
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ReadFileToolArguments))]
internal sealed partial class FileToolJsonContext : JsonSerializerContext
{
}
