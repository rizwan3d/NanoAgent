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
    private const string WriteTone = "\u001b[38;5;151m";
    private const string DiffAddTone = "\u001b[38;5;114m";
    private const string DiffRemoveTone = "\u001b[38;5;203m";
    private const string DiffHunkTone = "\u001b[38;5;117m";
    private const string DiffContextTone = "\u001b[38;5;250m";
    private const string CodeFenceTone = "\u001b[38;5;110m";
    private const string BashCodeTone = "\u001b[38;5;221m";
    private const string PowerShellCodeTone = "\u001b[38;5;150m";
    private const string GenericCodeTone = "\u001b[38;5;181m";
    private const string DividerGlyph = "\u2502";
    private const string ReturnGlyph = "\u23BF";
    private const string MiddleDot = "\u00B7";
    private const string DownArrow = "\u2193";
    private readonly object _consoleLock = new();
    private int _activeStatusRow = -1;
    private int _activeStatusWidth;
    private string? _activeStatusText;

    public void RenderHeader(AppConfig config)
    {
        lock (_consoleLock)
        {
            Console.Clear();
            Console.WriteLine();
            Console.WriteLine($"{Warm}  {config.AppName}{Reset}");
            Console.WriteLine($"{Muted}  Model:{Reset} {UserTone}{config.Model}{Reset}");
            Console.WriteLine($"{Muted}  Github:{Reset} {VerboseCommandTone}github.com/rizwan3d/NanoAgent{Reset}");
            Console.WriteLine($"{Muted}  Suponser:{Reset} {Warm}ALFAIN Technologies (PVT) Limited{Reset} {VerboseJsonTone}(https://alfain.co/){Reset}");
            Console.WriteLine($"{DividerTone}  {new string('\u2500', 53)}{Reset}");
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
            bool restoreStatusLine = SuspendActiveStatusLine();
            Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset} {Accent}>{Reset} {VerboseCommandTone}{command}{Reset}");
            ResumeActiveStatusLine(restoreStatusLine);
        }
    }

    public void RenderMutedToolCall(string toolName)
    {
        lock (_consoleLock)
        {
            bool restoreStatusLine = SuspendActiveStatusLine();
            Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset} {Muted}tool:{Reset} {Muted}{toolName}{Reset}");
            ResumeActiveStatusLine(restoreStatusLine);
        }
    }

    public void RenderFileOperationMessage(
        string operation,
        string path,
        string summary,
        IReadOnlyList<FilePreviewLine> previewLines,
        int hiddenLineCount)
    {
        lock (_consoleLock)
        {
            bool restoreStatusLine = SuspendActiveStatusLine();
            Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset} {Accent}{operation}{Reset}({VerboseCommandTone}{path}{Reset})");
            Console.WriteLine($"  {Muted}{ReturnGlyph}  {summary}{Reset}");

            foreach (FilePreviewLine previewLine in previewLines)
            {
                string number = previewLine.Number is int value
                    ? value.ToString().PadLeft(6)
                    : "      ";
                string lineTone = GetPreviewLineTone(operation, previewLine.Text);
                Console.WriteLine($"  {Muted}{number} {lineTone}{previewLine.Text}{Reset}");
            }

            if (hiddenLineCount > 0)
            {
                Console.WriteLine($"  {Muted}     ... +{hiddenLineCount} lines{Reset}");
            }

            ResumeActiveStatusLine(restoreStatusLine);
        }
    }

    public void BeginAgentActivity()
    {
        lock (_consoleLock)
        {
            ClearActiveStatusLine();
            _activeStatusRow = Console.CursorTop;
            _activeStatusWidth = 0;
            _activeStatusText = null;
            Console.WriteLine();
            RenderStatusLine($"(0s {MiddleDot} {DownArrow} estimating...)");
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
            _activeStatusText = null;
        }
    }

    public void RenderAgentMessage(string message)
    {
        lock (_consoleLock)
        {
            string[] responseLines = message.Replace("\r\n", "\n").Split('\n');
            bool inCodeBlock = false;
            string? codeLanguage = null;

            foreach (string line in responseLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset}");
                    continue;
                }

                if (TryToggleCodeFence(line, ref inCodeBlock, ref codeLanguage))
                {
                    Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset} {CodeFenceTone}{line}{Reset}");
                    continue;
                }

                string tone = inCodeBlock
                    ? GetCodeBlockTone(codeLanguage)
                    : AgentTone;
                Console.WriteLine($"{DividerTone}  {DividerGlyph}{Reset} {tone}{line}{Reset}");
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
        _activeStatusText = text;
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
        _activeStatusText = null;
    }

    private bool SuspendActiveStatusLine()
    {
        if (_activeStatusRow < 0)
        {
            return false;
        }

        (int left, int top) = Console.GetCursorPosition();
        Console.SetCursorPosition(0, _activeStatusRow);
        Console.Write(new string(' ', Math.Max(1, _activeStatusWidth)));
        Console.SetCursorPosition(left, top);
        _activeStatusRow = -1;
        _activeStatusWidth = 0;
        return !string.IsNullOrWhiteSpace(_activeStatusText);
    }

    private void ResumeActiveStatusLine(bool restoreStatusLine)
    {
        if (!restoreStatusLine || string.IsNullOrWhiteSpace(_activeStatusText))
        {
            return;
        }

        _activeStatusRow = Console.CursorTop;
        _activeStatusWidth = 0;
        Console.WriteLine();
        RenderStatusLine(_activeStatusText);
    }

    private static string FormatActivity(TimeSpan elapsed, int? outputTokens, bool isEstimate)
    {
        string time = FormatElapsed(elapsed);
        string tokens = outputTokens is int count
            ? $"{DownArrow} {FormatTokenCount(count)} tokens"
            : $"{DownArrow} estimating...";
        string estimateSuffix = outputTokens is int && isEstimate ? " est." : string.Empty;
        return $"({time} {MiddleDot} {tokens}{estimateSuffix})";
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

    private static string GetPreviewLineTone(string operation, string text)
    {
        if (string.Equals(operation, "Write", StringComparison.Ordinal))
        {
            return WriteTone;
        }

        if (string.Equals(operation, "Edit", StringComparison.Ordinal)
            || string.Equals(operation, "ApplyPatch", StringComparison.Ordinal))
        {
            if (text.StartsWith("@@", StringComparison.Ordinal))
            {
                return DiffHunkTone;
            }

            if (text.StartsWith("+", StringComparison.Ordinal) && !text.StartsWith("+++", StringComparison.Ordinal))
            {
                return DiffAddTone;
            }

            if (text.StartsWith("-", StringComparison.Ordinal) && !text.StartsWith("---", StringComparison.Ordinal))
            {
                return DiffRemoveTone;
            }

            if (text.StartsWith(" ", StringComparison.Ordinal))
            {
                return DiffContextTone;
            }
        }

        return AgentTone;
    }

    private static bool TryToggleCodeFence(string line, ref bool inCodeBlock, ref string? codeLanguage)
    {
        if (!line.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        if (!inCodeBlock)
        {
            codeLanguage = line["```".Length..].Trim().ToLowerInvariant();
            inCodeBlock = true;
            return true;
        }

        inCodeBlock = false;
        codeLanguage = null;
        return true;
    }

    private static string GetCodeBlockTone(string? codeLanguage)
    {
        return codeLanguage switch
        {
            "bash" or "sh" or "shell" or "zsh" => BashCodeTone,
            "powershell" or "ps1" or "pwsh" or "cmd" or "bat" => PowerShellCodeTone,
            _ => GenericCodeTone
        };
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
