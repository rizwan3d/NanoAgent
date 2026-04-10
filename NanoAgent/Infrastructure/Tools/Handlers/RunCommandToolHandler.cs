using System.Diagnostics;

namespace NanoAgent;

internal sealed class RunCommandToolHandler : IToolHandler
{
    public string Name => "run_command";

    public ChatToolDefinition Definition => new()
    {
        Function = new ChatToolFunctionDefinition
        {
            Name = Name,
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
    };

    public string Execute(ChatToolCall toolCall)
    {
        RunCommandToolArguments? arguments = ToolArgumentParser.Parse(
            toolCall,
            Name,
            FileToolJsonContext.Default.RunCommandToolArguments,
            out string? errorMessage);

        if (errorMessage is not null)
        {
            return errorMessage;
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Command))
        {
            return "Tool error: 'command' is required.";
        }

        try
        {
            ProcessStartInfo startInfo = ToolRuntime.CreateShellStartInfo(arguments.Command);
            string shellCommand = ToolRuntime.FormatShellCommand(startInfo);
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
}
