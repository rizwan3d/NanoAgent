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
        Console.WriteLine($"{VerboseTone}[verbose]{Reset} {Muted}{message}{Reset}");
    }
}
