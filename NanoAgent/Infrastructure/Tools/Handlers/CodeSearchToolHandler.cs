using System.Diagnostics;

namespace NanoAgent;

internal sealed class CodeSearchToolHandler : IToolHandler
{
    public string Name => "code_search";

    public ChatToolDefinition Definition => new()
    {
        Function = new ChatToolFunctionDefinition
        {
            Name = Name,
            Description = "Search code and text files for a pattern. Use this to find symbols, strings, or references across the project.",
            Parameters = new ChatToolParameters
            {
                AdditionalProperties = false,
                Required = ["pattern"],
                Properties = new Dictionary<string, ChatToolParameterProperty>
                {
                    ["pattern"] = new()
                    {
                        Type = "string",
                        Description = "The text or regex-style pattern to search for."
                    },
                    ["path"] = new()
                    {
                        Type = "string",
                        Description = "Optional relative or absolute file or directory path to limit the search scope."
                    }
                }
            }
        }
    };

    public string Execute(ChatToolCall toolCall)
    {
        CodeSearchToolArguments? arguments = ToolArgumentParser.Parse(
            toolCall,
            Name,
            FileToolJsonContext.Default.CodeSearchToolArguments,
            out string? errorMessage);

        if (errorMessage is not null)
        {
            return errorMessage;
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Pattern))
        {
            return ToolExecutionResults.Error(Name, "'pattern' is required.");
        }

        string scopePath = ToolRuntime.ResolveScope(arguments.Path);
        if (!File.Exists(scopePath) && !Directory.Exists(scopePath))
        {
            return ToolExecutionResults.Error(Name, "Search path not found.", result => result.Scope = scopePath);
        }

        try
        {
            if (ToolRuntime.IsCommandAvailable("rg"))
            {
                return ExecuteRipgrepSearch(arguments.Pattern, scopePath);
            }

            return ExecuteFallbackSearch(arguments.Pattern, scopePath);
        }
        catch (Exception exception)
        {
            return ToolExecutionResults.Error(Name, $"Unable to search. {exception.Message}", result =>
            {
                result.Pattern = arguments.Pattern;
                result.Scope = scopePath;
            });
        }
    }

    private static string ExecuteRipgrepSearch(string pattern, string scopePath)
    {
        string escapedPattern = OperatingSystem.IsWindows()
            ? pattern.Replace("\"", "\"\"")
            : pattern.Replace("\"", "\\\"");
        string escapedScopePath = OperatingSystem.IsWindows()
            ? scopePath.Replace("\"", "\"\"")
            : scopePath.Replace("\"", "\\\"");

        string command = $"rg -n --hidden --glob \"!.git\" \"{escapedPattern}\" \"{escapedScopePath}\"";
        ProcessStartInfo startInfo = ToolRuntime.CreateShellStartInfo(command);
        using Process process = new() { StartInfo = startInfo };

        process.Start();
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && process.ExitCode != 1)
        {
            return ToolExecutionResults.Error("code_search", $"code_search failed. {standardError.TrimEnd()}", result =>
            {
                result.Pattern = pattern;
                result.Scope = scopePath;
                result.Stderr = standardError.TrimEnd();
            });
        }

        string[] matches = string.IsNullOrWhiteSpace(standardOutput)
            ? []
            : standardOutput.TrimEnd().Split('\n');

        return ToolExecutionResults.Success("code_search", result =>
        {
            result.Pattern = pattern;
            result.Scope = scopePath;
            result.Matches = matches;
        });
    }

    private static string ExecuteFallbackSearch(string pattern, string scopePath)
    {
        IEnumerable<string> files = File.Exists(scopePath)
            ? [scopePath]
            : Directory.EnumerateFiles(scopePath, "*", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        List<string> matches = [];

        foreach (string filePath in files)
        {
            try
            {
                int lineNumber = 0;
                foreach (string line in File.ReadLines(filePath))
                {
                    lineNumber++;
                    if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add($"{filePath}:{lineNumber}:{line}");
                        if (matches.Count >= 200)
                        {
                            return ToolExecutionResults.Success("code_search", result =>
                            {
                                result.Pattern = pattern;
                                result.Scope = scopePath;
                                result.Matches = matches.ToArray();
                                result.Truncated = true;
                            });
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return ToolExecutionResults.Success("code_search", result =>
        {
            result.Pattern = pattern;
            result.Scope = scopePath;
            result.Matches = matches.ToArray();
        });
    }
}
