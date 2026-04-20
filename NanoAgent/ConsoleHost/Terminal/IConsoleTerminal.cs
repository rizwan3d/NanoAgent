namespace NanoAgent.ConsoleHost.Terminal;

internal interface IConsoleTerminal
{
    ConsoleColor BackgroundColor { get; set; }

    int CursorTop { get; }

    ConsoleColor ForegroundColor { get; set; }

    bool IsInputRedirected { get; }

    bool IsOutputRedirected { get; }

    int WindowHeight { get; }

    int WindowWidth { get; }

    ConsoleKeyInfo ReadKey(bool intercept);

    string? ReadLine();

    void ResetColor();

    void SetCursorPosition(int left, int top);

    void Write(string value);

    void WriteLine();

    void WriteLine(string value);
}
