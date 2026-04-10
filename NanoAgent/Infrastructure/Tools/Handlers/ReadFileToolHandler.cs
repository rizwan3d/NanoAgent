namespace NanoAgent;

internal sealed class ReadFileToolHandler : IToolHandler
{
    public string Name => "read_file";

    public ChatToolDefinition Definition => new()
    {
        Function = new ChatToolFunctionDefinition
        {
            Name = Name,
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
    };

    public string Execute(ChatToolCall toolCall)
    {
        ReadFileToolArguments? arguments = ToolArgumentParser.Parse(
            toolCall,
            Name,
            FileToolJsonContext.Default.ReadFileToolArguments,
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
}
