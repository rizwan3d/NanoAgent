namespace NanoAgent;

internal sealed class EditFileToolHandler : IToolHandler
{
    public string Name => "edit_file";

    public ChatToolDefinition Definition => new()
    {
        Function = new ChatToolFunctionDefinition
        {
            Name = Name,
            Description = "Edit an existing UTF-8 text file by replacing a specific text snippet with new text.",
            Parameters = new ChatToolParameters
            {
                AdditionalProperties = false,
                Required = ["path", "old_text", "new_text"],
                Properties = new Dictionary<string, ChatToolParameterProperty>
                {
                    ["path"] = new()
                    {
                        Type = "string",
                        Description = "Relative or absolute path to the file to edit."
                    },
                    ["old_text"] = new()
                    {
                        Type = "string",
                        Description = "Exact existing text to find in the file."
                    },
                    ["new_text"] = new()
                    {
                        Type = "string",
                        Description = "Replacement text to write into the file."
                    },
                    ["replace_all"] = new()
                    {
                        Type = "boolean",
                        Description = "Optional flag. When true, replace every match; otherwise replace exactly one match."
                    }
                }
            }
        }
    };

    public string Execute(ChatToolCall toolCall)
    {
        EditFileToolArguments? arguments = ToolArgumentParser.Parse(
            toolCall,
            Name,
            FileToolJsonContext.Default.EditFileToolArguments,
            out string? errorMessage);

        if (errorMessage is not null)
        {
            return errorMessage;
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Path))
        {
            return "Tool error: 'path' is required.";
        }

        if (string.IsNullOrEmpty(arguments.OldText))
        {
            return "Tool error: 'old_text' is required.";
        }

        string fullPath = ToolRuntime.ResolvePath(arguments.Path);
        if (!File.Exists(fullPath))
        {
            return $"Tool error: file not found: {fullPath}";
        }

        try
        {
            string content = File.ReadAllText(fullPath);
            string newline = ToolRuntime.DetectPreferredNewline(content);
            string normalizedContent = ToolRuntime.NormalizeNewlines(content);
            string normalizedOldText = ToolRuntime.NormalizeNewlines(arguments.OldText);
            string normalizedNewText = ToolRuntime.NormalizeNewlines(arguments.NewText);
            int matchCount = ToolRuntime.CountOccurrences(normalizedContent, normalizedOldText);

            if (matchCount == 0)
            {
                return $"Tool error: old_text was not found in file: {fullPath}";
            }

            if (!arguments.ReplaceAll && matchCount > 1)
            {
                return $"Tool error: old_text matched {matchCount} locations. Set replace_all=true or provide a more specific old_text.";
            }

            string updatedNormalizedContent = arguments.ReplaceAll
                ? normalizedContent.Replace(normalizedOldText, normalizedNewText)
                : ToolRuntime.ReplaceFirst(normalizedContent, normalizedOldText, normalizedNewText);
            string updatedContent = ToolRuntime.RestoreNewlines(updatedNormalizedContent, newline);

            File.WriteAllText(fullPath, updatedContent);
            return $"FILE_EDITED: {fullPath}";
        }
        catch (Exception exception)
        {
            return $"Tool error: unable to edit file '{fullPath}'. {exception.Message}";
        }
    }
}
