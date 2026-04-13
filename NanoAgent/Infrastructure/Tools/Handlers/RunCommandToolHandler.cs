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
                        Description = "The shell command to execute.",
                        MinLength = 1
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
            return ToolExecutionResults.Error(Name, "'command' is required.");
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

            return ToolExecutionResults.Success(Name, result =>
            {
                result.Command = arguments.Command;
                result.Shell = startInfo.FileName;
                result.Executed = shellCommand;
                result.Workdir = startInfo.WorkingDirectory;
                result.ExitCode = process.ExitCode;
                result.Stdout = output;
                result.Stderr = error;
            });
        }
        catch (Exception exception)
        {
            return ToolExecutionResults.Error(Name, $"Unable to run command. {exception.Message}", result => result.Command = arguments.Command);
        }
    }
}
