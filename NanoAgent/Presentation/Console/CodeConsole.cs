namespace NanoAgent;

internal sealed class CodeConsole : IChatConsole
{
    private const string Reset = "\u001b[0m";
    private const string Muted = "\u001b[38;5;244m";
    private const string Warm = "\u001b[38;5;180m";
    private const string Accent = "\u001b[38;5;216m";
    private const string UserTone = "\u001b[38;5;252m";
    private const string AgentTone = "\u001b[38;5;255m";
    private const string DividerTone = "\u001b[38;5;239m";
    private const string VerboseTone = "\u001b[38;5;117m";
    private const string VerboseLabelTone = "\u001b[38;5;81m";
    private const string VerboseCommandTone = "\u001b[38;5;221m";
    private const string VerboseShellTone = "\u001b[38;5;150m";
    private const string VerboseJsonTone = "\u001b[38;5;109m";

    public void RenderHeader(AppConfig config)
    {
        Console.Clear();
        Console.WriteLine();
        Console.WriteLine($"{Warm}  {config.AppName}{Reset}  {Muted}{config.Model}{Reset}");
        Console.WriteLine($"{DividerTone}  ─────────────────────────────────────────────────────{Reset}");
        Console.WriteLine($"{Muted}  Chat in the terminal. Press Ctrl+C to quit.{Reset}");
        Console.WriteLine();
    }

    public string? ReadUserInput()
    {
        Console.WriteLine($"{Accent}  >{Reset} {UserTone}message{Reset}");
        Console.Write($"{DividerTone}  │{Reset} ");
        return Console.ReadLine();
    }

    public void RenderUserMessage(string userInput)
    {
        Console.WriteLine($"{Muted}  {userInput.Trim()}{Reset}\n");
        Console.WriteLine($"{Warm}  NanoAgent{Reset}");
        Console.WriteLine($"{DividerTone}  │{Reset}");
    }

    public void RenderCommandMessage(string command)
    {
        Console.WriteLine($"{DividerTone}  â”‚{Reset} {Accent}>{Reset} {VerboseCommandTone}{command}{Reset}");
    }

    public void RenderAgentMessage(string message)
    {
        string[] responseLines = message.Replace("\r\n", "\n").Split('\n');

        foreach (string line in responseLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine($"{DividerTone}  │{Reset}");
                continue;
            }

            Console.WriteLine($"{DividerTone}  │{Reset} {AgentTone}{line}{Reset}");
        }

        Console.WriteLine($"{DividerTone}  │{Reset}");
        Console.WriteLine($"{Muted}  ready for the next message{Reset}\n");
    }

    public void RenderVerboseMessage(string message)
    {
        string normalized = message.Replace("\r\n", "\n");

        if (normalized.StartsWith("tool call: run_command\n", StringComparison.Ordinal))
        {
            RenderVerboseRunCommandCall(normalized);
            return;
        }

        if (normalized.StartsWith("tool result:\nCOMMAND:", StringComparison.Ordinal))
        {
            RenderVerboseRunCommandResult(normalized);
            return;
        }

        Console.WriteLine($"{VerboseTone}[verbose]{Reset} {Muted}{message}{Reset}");
    }

    private static void RenderVerboseRunCommandCall(string message)
    {
        string[] lines = message.Split('\n');
        Console.WriteLine($"{VerboseTone}[verbose]{Reset} {VerboseLabelTone}tool call{Reset} {Muted}run_command{Reset}");

        foreach (string line in lines.Skip(1))
        {
            if (line.StartsWith("command: ", StringComparison.Ordinal))
            {
                Console.WriteLine($"          {VerboseLabelTone}command:{Reset} {VerboseCommandTone}{line["command: ".Length..]}{Reset}");
                continue;
            }

            if (line.StartsWith("arguments: ", StringComparison.Ordinal))
            {
                Console.WriteLine($"          {VerboseLabelTone}arguments:{Reset} {VerboseJsonTone}{line["arguments: ".Length..]}{Reset}");
                continue;
            }

            Console.WriteLine($"          {Muted}{line}{Reset}");
        }
    }

    private static void RenderVerboseRunCommandResult(string message)
    {
        string[] lines = message.Split('\n');
        Console.WriteLine($"{VerboseTone}[verbose]{Reset} {VerboseLabelTone}tool result{Reset}");

        foreach (string line in lines.Skip(1))
        {
            if (line.StartsWith("COMMAND: ", StringComparison.Ordinal))
            {
                Console.WriteLine($"          {VerboseLabelTone}command:{Reset} {VerboseCommandTone}{line["COMMAND: ".Length..]}{Reset}");
                continue;
            }

            if (line.StartsWith("EXECUTED: ", StringComparison.Ordinal))
            {
                Console.WriteLine($"          {VerboseLabelTone}executed:{Reset} {VerboseShellTone}{line["EXECUTED: ".Length..]}{Reset}");
                continue;
            }

            if (line.StartsWith("SHELL: ", StringComparison.Ordinal))
            {
                Console.WriteLine($"          {VerboseLabelTone}shell:{Reset} {VerboseShellTone}{line["SHELL: ".Length..]}{Reset}");
                continue;
            }

            if (line.StartsWith("WORKDIR: ", StringComparison.Ordinal))
            {
                Console.WriteLine($"          {VerboseLabelTone}workdir:{Reset} {Muted}{line["WORKDIR: ".Length..]}{Reset}");
                continue;
            }

            if (line.StartsWith("EXIT_CODE: ", StringComparison.Ordinal))
            {
                Console.WriteLine($"          {VerboseLabelTone}exit code:{Reset} {Muted}{line["EXIT_CODE: ".Length..]}{Reset}");
                continue;
            }

            Console.WriteLine($"          {Muted}{line}{Reset}");
        }
    }
}
