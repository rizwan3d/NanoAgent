using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

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
        },
        new()
        {
            Function = new ChatToolFunctionDefinition
            {
                Name = "write_file",
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
        },
        new()
        {
            Function = new ChatToolFunctionDefinition
            {
                Name = "edit_file",
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
        },
        new()
        {
            Function = new ChatToolFunctionDefinition
            {
                Name = "apply_patch",
                Description = "Apply a multi-file unified diff patch. Use this for coordinated edits, file creation, file deletion, or larger structured changes.",
                Parameters = new ChatToolParameters
                {
                    AdditionalProperties = false,
                    Required = ["patch"],
                    Properties = new Dictionary<string, ChatToolParameterProperty>
                    {
                        ["patch"] = new()
                        {
                            Type = "string",
                            Description = "A unified diff patch string, typically starting with diff headers like ---/+++ or git diff format."
                        }
                    }
                }
            }
        },
        new()
        {
            Function = new ChatToolFunctionDefinition
            {
                Name = "code_search",
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
        },
        new()
        {
            Function = new ChatToolFunctionDefinition
            {
                Name = "run_command",
                Description = "Run a shell command in the current working directory. Uses PowerShell on Windows and bash on macOS/Linux when available.",
                Parameters = new ChatToolParameters
                {
                    AdditionalProperties = false,
                    Required = ["command"],
                    Properties = new Dictionary<string, ChatToolParameterProperty>
                    {
                        ["command"] = new()
                        {
                            Type = "string",
                            Description = "The shell command to execute."
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
            "write_file" => ExecuteWriteFile(toolCall),
            "edit_file" => ExecuteEditFile(toolCall),
            "apply_patch" => ExecuteApplyPatch(toolCall),
            "code_search" => ExecuteCodeSearch(toolCall),
            "run_command" => ExecuteRunCommand(toolCall),
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

    private static string ExecuteRunCommand(ChatToolCall toolCall)
    {
        RunCommandToolArguments? arguments;
        try
        {
            arguments = JsonSerializer.Deserialize(
                toolCall.Function.Arguments,
                FileToolJsonContext.Default.RunCommandToolArguments);
        }
        catch (JsonException exception)
        {
            return $"Tool error: invalid arguments for run_command. {exception.Message}";
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Command))
        {
            return "Tool error: 'command' is required.";
        }

        try
        {
            ProcessStartInfo startInfo = CreateShellStartInfo(arguments.Command);
            string shellCommand = FormatShellCommand(startInfo);
            using Process process = new() { StartInfo = startInfo };

            process.Start();
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            string output = string.IsNullOrWhiteSpace(standardOutput) ? "<empty>" : standardOutput.TrimEnd();
            string error = string.IsNullOrWhiteSpace(standardError) ? "<empty>" : standardError.TrimEnd();

            return
                $"COMMAND: {arguments.Command}\n" +
                $"SHELL: {startInfo.FileName}\n" +
                $"EXECUTED: {shellCommand}\n" +
                $"WORKDIR: {startInfo.WorkingDirectory}\n" +
                $"EXIT_CODE: {process.ExitCode}\n" +
                $"STDOUT:\n{output}\n" +
                $"STDERR:\n{error}";
        }
        catch (Exception exception)
        {
            return $"Tool error: unable to run command '{arguments.Command}'. {exception.Message}";
        }
    }

    private static string ExecuteWriteFile(ChatToolCall toolCall)
    {
        WriteFileToolArguments? arguments;
        try
        {
            arguments = JsonSerializer.Deserialize(
                toolCall.Function.Arguments,
                FileToolJsonContext.Default.WriteFileToolArguments);
        }
        catch (JsonException exception)
        {
            return $"Tool error: invalid arguments for write_file. {exception.Message}";
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Path))
        {
            return "Tool error: 'path' is required.";
        }

        string fullPath = ResolvePath(arguments.Path);

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

    private static string ExecuteEditFile(ChatToolCall toolCall)
    {
        EditFileToolArguments? arguments;
        try
        {
            arguments = JsonSerializer.Deserialize(
                toolCall.Function.Arguments,
                FileToolJsonContext.Default.EditFileToolArguments);
        }
        catch (JsonException exception)
        {
            return $"Tool error: invalid arguments for edit_file. {exception.Message}";
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Path))
        {
            return "Tool error: 'path' is required.";
        }

        if (string.IsNullOrEmpty(arguments.OldText))
        {
            return "Tool error: 'old_text' is required.";
        }

        string fullPath = ResolvePath(arguments.Path);
        if (!File.Exists(fullPath))
        {
            return $"Tool error: file not found: {fullPath}";
        }

        try
        {
            string content = File.ReadAllText(fullPath);
            string newline = DetectPreferredNewline(content);
            string normalizedContent = NormalizeNewlines(content);
            string normalizedOldText = NormalizeNewlines(arguments.OldText);
            string normalizedNewText = NormalizeNewlines(arguments.NewText);
            int matchCount = CountOccurrences(normalizedContent, normalizedOldText);

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
                : ReplaceFirst(normalizedContent, normalizedOldText, normalizedNewText);
            string updatedContent = RestoreNewlines(updatedNormalizedContent, newline);

            File.WriteAllText(fullPath, updatedContent);
            return $"FILE_EDITED: {fullPath}";
        }
        catch (Exception exception)
        {
            return $"Tool error: unable to edit file '{fullPath}'. {exception.Message}";
        }
    }

    private static string ExecuteCodeSearch(ChatToolCall toolCall)
    {
        CodeSearchToolArguments? arguments;
        try
        {
            arguments = JsonSerializer.Deserialize(
                toolCall.Function.Arguments,
                FileToolJsonContext.Default.CodeSearchToolArguments);
        }
        catch (JsonException exception)
        {
            return $"Tool error: invalid arguments for code_search. {exception.Message}";
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Pattern))
        {
            return "Tool error: 'pattern' is required.";
        }

        string scopePath = ResolveScope(arguments.Path);
        if (!File.Exists(scopePath) && !Directory.Exists(scopePath))
        {
            return $"Tool error: search path not found: {scopePath}";
        }

        try
        {
            if (IsCommandAvailable("rg"))
            {
                return ExecuteRipgrepSearch(arguments.Pattern, scopePath);
            }

            return ExecuteFallbackSearch(arguments.Pattern, scopePath);
        }
        catch (Exception exception)
        {
            return $"Tool error: unable to search for '{arguments.Pattern}'. {exception.Message}";
        }
    }

    private static string ExecuteApplyPatch(ChatToolCall toolCall)
    {
        ApplyPatchToolArguments? arguments;
        try
        {
            arguments = JsonSerializer.Deserialize(
                toolCall.Function.Arguments,
                FileToolJsonContext.Default.ApplyPatchToolArguments);
        }
        catch (JsonException exception)
        {
            return $"Tool error: invalid arguments for apply_patch. {exception.Message}";
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Patch))
        {
            return "Tool error: 'patch' is required.";
        }

        try
        {
            if (!IsCommandAvailable("git"))
            {
                return "Tool error: git is required for apply_patch but was not found.";
            }

            string tempPatchPath = Path.Combine(Path.GetTempPath(), $"nanoagent-{Guid.NewGuid():N}.patch");
            File.WriteAllText(tempPatchPath, arguments.Patch);

            try
            {
                ProcessStartInfo startInfo = CreateGitApplyStartInfo(tempPatchPath);
                using Process process = new() { StartInfo = startInfo };

                process.Start();
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string error = string.IsNullOrWhiteSpace(standardError) ? "<empty>" : standardError.TrimEnd();
                    return $"Tool error: apply_patch failed.\nSTDERR:\n{error}";
                }

                string output = string.IsNullOrWhiteSpace(standardOutput) ? "<empty>" : standardOutput.TrimEnd();
                return $"PATCH_APPLIED\nSTDOUT:\n{output}";
            }
            finally
            {
                if (File.Exists(tempPatchPath))
                {
                    File.Delete(tempPatchPath);
                }
            }
        }
        catch (Exception exception)
        {
            return $"Tool error: unable to apply patch. {exception.Message}";
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

    private static string ResolveScope(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Environment.CurrentDirectory : ResolvePath(path);

    private static ProcessStartInfo CreateShellStartInfo(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -EncodedCommand {EncodePowerShellCommand(command)}",
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        string shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
        return new ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"-lc {EscapePosix(command)}",
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static string EscapePosix(string command) =>
        $"'{command.Replace("'", "'\"'\"'")}'";

    private static string EncodePowerShellCommand(string command)
    {
        string wrappedCommand = $"& {{ {command} }}";
        byte[] bytes = Encoding.Unicode.GetBytes(wrappedCommand);
        return Convert.ToBase64String(bytes);
    }

    private static string FormatShellCommand(ProcessStartInfo startInfo)
    {
        if (string.IsNullOrWhiteSpace(startInfo.Arguments))
        {
            return startInfo.FileName;
        }

        return $"{startInfo.FileName} {startInfo.Arguments}";
    }

    private static int CountOccurrences(string content, string value)
    {
        int count = 0;
        int index = 0;

        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReplaceFirst(string content, string oldValue, string newValue)
    {
        int index = content.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0)
        {
            return content;
        }

        return string.Concat(
            content.AsSpan(0, index),
            newValue,
            content.AsSpan(index + oldValue.Length));
    }

    private static string NormalizeNewlines(string content) =>
        content.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string DetectPreferredNewline(string content) =>
        content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string RestoreNewlines(string content, string newline) =>
        newline == "\n" ? content : content.Replace("\n", newline, StringComparison.Ordinal);

    private static bool IsCommandAvailable(string commandName)
    {
        try
        {
            ProcessStartInfo startInfo;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = commandName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-lc 'command -v {commandName}'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static ProcessStartInfo CreateGitApplyStartInfo(string patchPath) =>
        new()
        {
            FileName = "git",
            Arguments = $"apply --whitespace=nowarn --recount \"{patchPath.Replace("\"", "\"\"")}\"",
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

    private static string ExecuteRipgrepSearch(string pattern, string scopePath)
    {
        string escapedPattern = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? pattern.Replace("\"", "\"\"")
            : pattern.Replace("\"", "\\\"");
        string escapedScopePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? scopePath.Replace("\"", "\"\"")
            : scopePath.Replace("\"", "\\\"");

        string command = $"rg -n --hidden --glob \"!.git\" \"{escapedPattern}\" \"{escapedScopePath}\"";
        ProcessStartInfo startInfo = CreateShellStartInfo(command);
        using Process process = new() { StartInfo = startInfo };

        process.Start();
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 1 && string.IsNullOrWhiteSpace(standardError))
        {
            return $"SEARCH: {pattern}\nSCOPE: {scopePath}\n<no matches>";
        }

        if (process.ExitCode != 0 && process.ExitCode != 1)
        {
            return $"Tool error: code_search failed.\nSTDERR:\n{standardError.TrimEnd()}";
        }

        string output = string.IsNullOrWhiteSpace(standardOutput) ? "<no matches>" : standardOutput.TrimEnd();
        return $"SEARCH: {pattern}\nSCOPE: {scopePath}\n{output}";
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
                            return $"SEARCH: {pattern}\nSCOPE: {scopePath}\n{string.Join('\n', matches)}\n<results truncated>";
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return matches.Count == 0
            ? $"SEARCH: {pattern}\nSCOPE: {scopePath}\n<no matches>"
            : $"SEARCH: {pattern}\nSCOPE: {scopePath}\n{string.Join('\n', matches)}";
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ReadFileToolArguments))]
[JsonSerializable(typeof(ListFilesToolArguments))]
[JsonSerializable(typeof(RunCommandToolArguments))]
[JsonSerializable(typeof(WriteFileToolArguments))]
[JsonSerializable(typeof(EditFileToolArguments))]
[JsonSerializable(typeof(CodeSearchToolArguments))]
[JsonSerializable(typeof(ApplyPatchToolArguments))]
internal sealed partial class FileToolJsonContext : JsonSerializerContext
{
}
