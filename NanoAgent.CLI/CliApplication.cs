using NanoAgent.Application.Backend;
using NanoAgent.Infrastructure.Telemetry;

namespace NanoAgent.CLI;

internal static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (SandboxInvocationParser.TryHandleSpecialInvocation(args, out int specialExitCode))
        {
            return specialExitCode;
        }

        CliInvocation invocation;
        try
        {
            invocation = CliInvocation.Parse(
                args,
                Console.IsInputRedirected,
                Console.In.ReadToEnd);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            WriteUsage(Console.Error);
            return ExitCodeMapper.UsageError;
        }

        if (invocation.ShowHelp)
        {
            WriteUsage(Console.Out);
            return ExitCodeMapper.Success;
        }

        if (invocation.ShowVersion)
        {
            Console.Out.WriteLine(GetVersionText());
            return ExitCodeMapper.Success;
        }

        BackendRuntimeArguments runtimeArguments = invocation.RuntimeArguments.WithDefaults(
            BackendRuntimeOptions.CliSurface);

        return invocation.Mode switch
        {
            CliMode.SingleTurn => await SingleTurnRunner.RunAsync(
                runtimeArguments,
                invocation.ProviderAuthKey,
                invocation.Prompt ?? string.Empty,
                invocation.JsonOutput,
                invocation.AutoApproveAllTools),
            CliMode.Acp => await AcpHost.RunAsync(
                runtimeArguments.RawArgs,
                invocation.ProviderAuthKey,
                invocation.NoOldReader,
                invocation.AutoApproveAllTools),
            _ => await InteractiveTerminalHost.RunAsync(
                runtimeArguments,
                invocation.ProviderAuthKey,
                invocation.NoOldReader,
                invocation.AutoApproveAllTools)
        };
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine(
            $"""
            {GetVersionText()}

            Usage:
              nanoai [options]                    Start the interactive terminal UI
              nanoai [options] "<prompt>"         Run one prompt and print the response
              nanoai [options] --prompt "<text>"  Run one prompt and print the response
              echo "<prompt>" | nanoai [options]  Run one prompt from standard input
              nanoai --acp [options]              Run an Agent Client Protocol server

            Options:
              --acp                Speak ACP over stdin/stdout for compatible editors
              --interactive        Start the terminal UI explicitly
              --stdin              Read the one-shot prompt from standard input
              --json               Write one-shot result as a JSON object
              -y, --yes            Approve promptable tool requests for this run
              -p, --prompt <text>  One-shot prompt text
              --sandbox-mode <mode>
                                   Override sandbox mode: read-only, workspace-write, or danger-full-access
              --provider-auth-key <key>
                                   Use this key for provider API-key onboarding
              --section <id>       Resume an existing section
              --session <id>       Alias for --section
              --no-update-check    Skip checking for application updates on startup
              --no-old-reader      Resume a section without replaying old messages to the screen
              --profile <name>     Use an agent profile
              --thinking <on|off>  Override thinking mode
              -v, --version        Show version
              -h, --help           Show help
              --doctor             Run system diagnostics and print doctor report

            Note:
              Run nanoai once to complete provider setup before using one-shot prompts.
            """);
    }

    internal static string GetVersionText()
    {
        return $"NanoAgent CLI {ProductTelemetryHelpers.GetNanoAgentVersion()}";
    }
}
