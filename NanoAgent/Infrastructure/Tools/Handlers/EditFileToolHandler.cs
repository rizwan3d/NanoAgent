using System.Diagnostics;

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
            string diff = BuildDiff(content, updatedContent);
            return $"FILE_EDITED: {fullPath}\nDIFF:\n{diff}";
        }
        catch (Exception exception)
        {
            return $"Tool error: unable to edit file '{fullPath}'. {exception.Message}";
        }
    }

    private static string BuildDiff(string originalContent, string updatedContent)
    {
        if (!ToolRuntime.IsCommandAvailable("git"))
        {
            return "<git unavailable>";
        }

        string originalPath = Path.Combine(Path.GetTempPath(), $"nanoagent-before-{Guid.NewGuid():N}.tmp");
        string updatedPath = Path.Combine(Path.GetTempPath(), $"nanoagent-after-{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(originalPath, originalContent);
            File.WriteAllText(updatedPath, updatedContent);

            ProcessStartInfo startInfo = ToolRuntime.CreateGitDiffNoIndexStartInfo(originalPath, updatedPath);
            using Process process = new() { StartInfo = startInfo };
            process.Start();
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode is not 0 and not 1)
            {
                return string.IsNullOrWhiteSpace(standardError) ? "<diff unavailable>" : standardError.TrimEnd();
            }

            return string.IsNullOrWhiteSpace(standardOutput) ? "<no diff>" : standardOutput.TrimEnd();
        }
        finally
        {
            if (File.Exists(originalPath))
            {
                File.Delete(originalPath);
            }

            if (File.Exists(updatedPath))
            {
                File.Delete(updatedPath);
            }
        }
    }
}
