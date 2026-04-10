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
    private const string DividerGlyph = "│";
    private readonly object _consoleLock = new();
    private int _activeStatusRow = -1;
    private int _activeStatusWidth;

    public void RenderHeader(AppConfig config)
    {
        lock (_consoleLock)
        {
            Console.Clear();
            Console.WriteLine();
            Console.WriteLine($"{Warm}  {config.AppName}{Reset}  {Muted}{config.Model}{Reset}");
            Console.WriteLine($"{DividerTone}  {new string('─', 53)}{Reset}");
            Console.WriteLine($"{Muted}  Chat in the terminal. Press Ctrl+C to quit.{Reset}");
            Console.WriteLine();
        }
    }

    public string? ReadUserInput()
    {
        lock (_consoleLock)
        {
            Console.WriteLine($"{Accent}  >{Reset} {UserTone}message{Reset}");
            Console.Write($"{DividerTone}  {DividerGlyph}{Reset} ");
            return Console.ReadLine();
        }
    }

    public void RenderUserMessage(string userInput)
    {
        lock (_consoleLock)
        {
            ClearActiveStatusLine();
            Console.WriteLine($"{Muted}  {userInput.Trim()}{Reset}\n");
            Console.WriteLine($"{Warm}  NanoAgent{Reset}");
            Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset}");
        }
    }

    public void RenderCommandMessage(string command)
    {
        lock (_consoleLock)
        {
            ClearActiveStatusLine();
            Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset} {Accent}>{Reset} {VerboseCommandTone}{command}{Reset}");
        }
    }

    public void BeginAgentActivity()
    {
        lock (_consoleLock)
        {
            ClearActiveStatusLine();
            _activeStatusRow = Console.CursorTop;
            _activeStatusWidth = 0;
            Console.WriteLine();
            RenderStatusLine("(0s · ↓ estimating...)");
        }
    }

    public void UpdateAgentActivity(TimeSpan elapsed, int? outputTokens, bool isEstimate)
    {
        lock (_consoleLock)
        {
            if (_activeStatusRow < 0)
            {
                return;
            }

            RenderStatusLine(FormatActivity(elapsed, outputTokens, isEstimate));
        }
    }

    public void CompleteAgentActivity(TimeSpan elapsed, int? outputTokens, bool isEstimate)
    {
        lock (_consoleLock)
        {
            if (_activeStatusRow < 0)
            {
                return;
            }

            RenderStatusLine(FormatActivity(elapsed, outputTokens, isEstimate));
            _activeStatusRow = -1;
            _activeStatusWidth = 0;
        }
    }

    public void RenderAgentMessage(string message)
    {
        lock (_consoleLock)
        {
            string[] responseLines = message.Replace("\r\n", "\n").Split('\n');

            foreach (string line in responseLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset}");
                    continue;
                }

                Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset} {AgentTone}{line}{Reset}");
            }

            Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset}");
            Console.WriteLine($"{Muted}  ready for the next message{Reset}\n");
        }
    }

    public void RenderVerboseMessage(string message)
    {
        lock (_consoleLock)
        {
            ClearActiveStatusLine();
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
    }

    private void RenderStatusLine(string text)
    {
        if (_activeStatusRow < 0)
        {
            return;
        }

        (int left, int top) = Console.GetCursorPosition();
        Console.SetCursorPosition(0, _activeStatusRow);
        string rendered = $"{Muted}  {text}{Reset}";
        int desiredWidth = text.Length + 2;
        Console.Write(rendered);

        int trailingSpaces = Math.Max(0, Math.Max(_activeStatusWidth, desiredWidth) - desiredWidth);
        if (trailingSpaces > 0)
        {
            Console.Write(new string(' ', trailingSpaces));
        }

        _activeStatusWidth = desiredWidth;
        Console.SetCursorPosition(left, top);
    }

    private void ClearActiveStatusLine()
    {
        if (_activeStatusRow < 0)
        {
            return;
        }

        (int left, int top) = Console.GetCursorPosition();
        Console.SetCursorPosition(0, _activeStatusRow);
        Console.Write(new string(' ', Math.Max(1, _activeStatusWidth)));
        Console.SetCursorPosition(left, top);
        _activeStatusRow = -1;
        _activeStatusWidth = 0;
    }

    private static string FormatActivity(TimeSpan elapsed, int? outputTokens, bool isEstimate)
    {
        string time = FormatElapsed(elapsed);
        string tokens = outputTokens is int count
            ? $"↓ {FormatTokenCount(count)} tokens"
            : "↓ estimating...";
        string estimateSuffix = outputTokens is int && isEstimate ? " est." : string.Empty;
        return $"({time} · {tokens}{estimateSuffix})";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return $"{elapsed.Minutes}m {elapsed.Seconds}s";
        }

        return $"{elapsed.Seconds}s";
    }

    private static string FormatTokenCount(int count)
    {
        if (count >= 1_000_000)
        {
            return $"{count / 1_000_000d:0.#}M";
        }

        if (count >= 1_000)
        {
            return $"{count / 1_000d:0.#}k";
        }

        return count.ToString();
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
