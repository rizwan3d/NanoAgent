namespace NanoAgent.Presentation.Cli.Terminal;

internal interface IConsolePromptInputReader
{
    Task<string> ReadLineAsync(
        string? defaultValue,
        ConsoleInputEchoMode echoMode,
        bool allowCancellation,
        CancellationToken cancellationToken);
}
