namespace NanoAgent;

internal sealed class WriteFileToolHandler : IToolHandler
{
    public string Name => "write_file";

    public ChatToolDefinition Definition => new()
    {
        Function = new ChatToolFunctionDefinition
        {
            Name = Name,
            Description = "Create or overwrite a UTF-8 text file on disk. Use this to generate code or update source files.",
            Parameters = new ChatToolParameters
            {
                AdditionalProperties = false,
                Required = ["path", "content"],
                Properties = new Dictionary<string, ChatToolParameterProperty>
                {
                    ["path"] = new()
                    {
                        Type = "string",
                        Description = "Relative or absolute path to the file to create or overwrite."
                    },
                    ["content"] = new()
                    {
                        Type = "string",
                        Description = "Full UTF-8 text content to write into the file."
                    }
                }
            }
        }
    };

    public string Execute(ChatToolCall toolCall)
    {
        WriteFileToolArguments? arguments = ToolArgumentParser.Parse(
            toolCall,
            Name,
            FileToolJsonContext.Default.WriteFileToolArguments,
            out string? errorMessage);

        if (errorMessage is not null)
        {
            return errorMessage;
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Path))
        {
            return "Tool error: 'path' is required.";
        }

        string fullPath = ToolRuntime.ResolvePath(arguments.Path);

        try
        {
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, arguments.Content ?? string.Empty);
            return $"FILE_WRITTEN: {fullPath}";
        }
        catch (Exception exception)
        {
            return $"Tool error: unable to write file '{fullPath}'. {exception.Message}";
        }
    }
}
