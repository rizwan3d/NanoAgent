using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Runtime.InteropServices;

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
                $"EXIT_CODE: {process.ExitCode}\n" +
                $"STDOUT:\n{output}\n" +
                $"STDERR:\n{error}";
        }
        catch (Exception exception)
        {
            return $"Tool error: unable to run command '{arguments.Command}'. {exception.Message}";
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

    private static ProcessStartInfo CreateShellStartInfo(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command {EscapePowerShell(command)}",
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

    private static string EscapePowerShell(string command) =>
        $"'{command.Replace("'", "''")}'";

    private static string EscapePosix(string command) =>
        $"'{command.Replace("'", "'\"'\"'")}'";
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ReadFileToolArguments))]
[JsonSerializable(typeof(ListFilesToolArguments))]
[JsonSerializable(typeof(RunCommandToolArguments))]
internal sealed partial class FileToolJsonContext : JsonSerializerContext
{
}
