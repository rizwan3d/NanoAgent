using System.Diagnostics;

namespace NanoAgent;

internal sealed class ApplyPatchToolHandler : IToolHandler
{
    public string Name => "apply_patch";

    public ChatToolDefinition Definition => new()
    {
        Function = new ChatToolFunctionDefinition
        {
            Name = Name,
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
    };

    public string Execute(ChatToolCall toolCall)
    {
        ApplyPatchToolArguments? arguments = ToolArgumentParser.Parse(
            toolCall,
            Name,
            FileToolJsonContext.Default.ApplyPatchToolArguments,
            out string? errorMessage);

        if (errorMessage is not null)
        {
            return errorMessage;
        }

        if (arguments is null || string.IsNullOrWhiteSpace(arguments.Patch))
        {
            return "Tool error: 'patch' is required.";
        }

        try
        {
            if (!ToolRuntime.IsCommandAvailable("git"))
            {
                return "Tool error: git is required for apply_patch but was not found.";
            }

            string tempPatchPath = Path.Combine(Path.GetTempPath(), $"nanoagent-{Guid.NewGuid():N}.patch");
            File.WriteAllText(tempPatchPath, arguments.Patch);

            try
            {
                ProcessStartInfo startInfo = ToolRuntime.CreateGitApplyStartInfo(tempPatchPath);
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
}
