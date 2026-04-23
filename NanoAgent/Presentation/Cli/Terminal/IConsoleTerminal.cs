namespace NanoAgent.Presentation.Cli.Terminal;

internal interface IConsoleTerminal
{
    int CursorLeft { get; }

    int CursorTop { get; }

    bool IsInputRedirected { get; }

    bool IsOutputRedirected { get; }

    bool KeyAvailable { get; }

    int WindowHeight { get; }

    int WindowWidth { get; }

    ConsoleKeyInfo ReadKey(bool intercept);

    string? ReadLine();

    void SetCursorPosition(int left, int top);

    void Write(string value);

    void WriteLine();

    void WriteLine(string value);
}
