using NanoAgent.Infrastructure.WindowsSandbox;

namespace NanoAgent.CLI;

internal static class SandboxInvocationParser
{
    public static bool TryHandleSpecialInvocation(
        IReadOnlyList<string> args,
        out int exitCode)
    {
        if (TryHandleSetupInvocation(args, out exitCode))
        {
            return true;
        }

        return TryHandleRunnerInvocation(args, out exitCode);
    }

    private static bool TryHandleSetupInvocation(
        IReadOnlyList<string> args,
        out int exitCode)
    {
        exitCode = 0;

        int commandIndex = -1;
        for (int index = 0; index < args.Count; index++)
        {
            if (string.Equals(
                    args[index],
                    WindowsSandboxSetupOrchestrator.SetupCommandArgument,
                    StringComparison.Ordinal))
            {
                commandIndex = index;
                break;
            }
        }

        if (commandIndex < 0)
        {
            return false;
        }

        int payloadIndex = commandIndex + 1;
        if (payloadIndex >= args.Count || string.IsNullOrWhiteSpace(args[payloadIndex]))
        {
            Console.Error.WriteLine("Missing setup payload for Windows sandbox setup mode.");
            exitCode = ExitCodeMapper.UsageError;
            return true;
        }

        exitCode = WindowsSandboxSetupOrchestrator.RunEncodedSetupPayload(args[payloadIndex]);
        return true;
    }

    private static bool TryHandleRunnerInvocation(
        IReadOnlyList<string> args,
        out int exitCode)
    {
        exitCode = 0;

        if (!args.Any(arg => string.Equals(
                arg,
                WindowsSandboxProcessRunner.RunnerCommandArgument,
                StringComparison.Ordinal)))
        {
            return false;
        }

        if (!TryReadRunnerPipeArgument(args, "--pipe-in", out string? pipeIn) ||
            !TryReadRunnerPipeArgument(args, "--pipe-out", out string? pipeOut))
        {
            Console.Error.WriteLine("Missing required pipe arguments for Windows sandbox runner mode.");
            exitCode = ExitCodeMapper.UsageError;
            return true;
        }

        exitCode = WindowsSandboxProcessRunner.RunPipeRunner(
            WindowsSandboxRunnerClient.ParsePipeArgument(pipeIn!),
            WindowsSandboxRunnerClient.ParsePipeArgument(pipeOut!));
        return true;
    }

    private static bool TryReadRunnerPipeArgument(
        IReadOnlyList<string> args,
        string optionName,
        out string? value)
    {
        value = null;

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
            {
                int valueIndex = index + 1;
                if (valueIndex >= args.Count || string.IsNullOrWhiteSpace(args[valueIndex]))
                {
                    return false;
                }

                value = args[valueIndex].Trim();
                return true;
            }

            string prefix = optionName + "=";
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string candidate = arg[prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            value = candidate;
            return true;
        }

        return false;
    }
}
