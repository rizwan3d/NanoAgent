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
                        Description = "Relative or absolute path to a directory.",
                        MinLength = 1
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
            return ToolExecutionResults.Error(Name, "'path' is required.");
        }

        string directoryPath = ToolRuntime.ResolvePath(arguments.Path);
        if (!Directory.Exists(directoryPath))
        {
            return ToolExecutionResults.Error(Name, "Directory not found.", result => result.Path = directoryPath);
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

            return ToolExecutionResults.Success(Name, result =>
            {
                result.Path = directoryPath;
                result.Entries = entries;
            });
        }
        catch (Exception exception)
        {
            return ToolExecutionResults.Error(Name, $"Unable to list directory. {exception.Message}", result => result.Path = directoryPath);
        }
    }
}
