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
        },
        new()
        {
            Function = new ChatToolFunctionDefinition
            {
                Name = "list_files",
                Description = "List files and directories inside a folder. Use this to discover project structure before reading files.",
                Parameters = new ChatToolParameters
                {
                    AdditionalProperties = false,
                    Required = ["path"],
                    Properties = new Dictionary<string, ChatToolParameterProperty>
                    {
                        ["path"] = new()
                        {
                            Type = "string",
                            Description = "Relative or absolute path to a directory."
                        }
                    }
                }
            }
        }
    ];

    public string Execute(ChatToolCall toolCall) =>
        toolCall.Function.Name switch
        {
            "read_file" => ExecuteReadFile(toolCall),
            "list_files" => ExecuteListFiles(toolCall),
            _ => $"Tool error: unsupported tool '{toolCall.Function.Name}'."
        };

    private static string ExecuteReadFile(ChatToolCall toolCall)
    {
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

    private static string ExecuteListFiles(ChatToolCall toolCall)
    {
        ListFilesToolArguments? arguments;
        try
        {
            arguments = JsonSerializer.Deserialize(
                toolCall.Function.Arguments,
                FileToolJsonContext.Default.ListFilesToolArguments);
        }
        catch (JsonException exception)
        {
            return $"Tool error: invalid arguments for list_files. {exception.Message}";
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Path))
        {
            return "Tool error: 'path' is required.";
        }

        string directoryPath = ResolvePath(arguments.Path);
        if (!Directory.Exists(directoryPath))
        {
            return $"Tool error: directory not found: {directoryPath}";
        }

        try
        {
            string[] entries = Directory
                .EnumerateFileSystemEntries(directoryPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    bool isDirectory = Directory.Exists(path);
                    string name = Path.GetFileName(path);
                    return isDirectory ? $"DIR  {name}" : $"FILE {name}";
                })
                .ToArray();

            return entries.Length == 0
                ? $"DIRECTORY: {directoryPath}\n<empty>"
                : $"DIRECTORY: {directoryPath}\n{string.Join('\n', entries)}";
        }
        catch (Exception exception)
        {
            return $"Tool error: unable to list directory '{directoryPath}'. {exception.Message}";
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
[JsonSerializable(typeof(ListFilesToolArguments))]
internal sealed partial class FileToolJsonContext : JsonSerializerContext
{
}
