namespace NanoAgent;

internal sealed class ListFilesToolHandler : IToolHandler
{
    public string Name => "list_files";

    public ChatToolDefinition Definition => new()
    {
        Function = new ChatToolFunctionDefinition
        {
            Name = Name,
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
    };

    public string Execute(ChatToolCall toolCall)
    {
        ListFilesToolArguments? arguments = ToolArgumentParser.Parse(
            toolCall,
            Name,
            FileToolJsonContext.Default.ListFilesToolArguments,
            out string? errorMessage);

        if (errorMessage is not null)
        {
            return errorMessage;
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Path))
        {
            return "Tool error: 'path' is required.";
        }

        string directoryPath = ToolRuntime.ResolvePath(arguments.Path);
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
}
