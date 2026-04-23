using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Configuration;
using NanoAgent.Presentation.Abstractions;

namespace NanoAgent.Presentation.Cli.Repl;

internal sealed class ConsoleReplInputReader : IReplInputReader
{
    private const string MultilineDelimiter = "\"\"\"";

    private readonly Terminal.IConsoleTerminal _terminal;
    private readonly string _prompt;
    private readonly string _continuationPrompt;

    public ConsoleReplInputReader(Terminal.IConsoleTerminal terminal)
    {
        _terminal = terminal;
        _prompt = BuildPrompt(ApplicationIdentity.ProductName);
        _continuationPrompt = BuildContinuationPrompt();
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? firstLine = ReadPromptedLine(_prompt, cancellationToken);
        if (!IsMultilineDelimiter(firstLine))
        {
            return Task.FromResult(firstLine);
        }

        return Task.FromResult(ReadMultilineInput(cancellationToken));
    }

    private static string BuildPrompt(string productName)
    {
        string normalizedName = string.IsNullOrWhiteSpace(productName)
            ? "agent"
            : productName.Trim().ToLowerInvariant();

        return $"\u001b[38;5;244m{normalizedName}>\u001b[0m ";
    }

    private static string BuildContinuationPrompt()
    {
        return "\u001b[38;5;244m...>\u001b[0m ";
    }

    private string? ReadPromptedLine(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_terminal.IsOutputRedirected)
        {
            _terminal.Write(prompt);
        }

        return _terminal.ReadLine();
    }

    private string? ReadMultilineInput(CancellationToken cancellationToken)
    {
        List<string> lines = [];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = ReadPromptedLine(_continuationPrompt, cancellationToken);
            if (line is null)
            {
                return lines.Count == 0
                    ? null
                    : string.Join(Environment.NewLine, lines);
            }

            if (IsMultilineDelimiter(line))
            {
                return string.Join(Environment.NewLine, lines);
            }

            lines.Add(line);
        }
    }

    private static bool IsMultilineDelimiter(string? value)
    {
        return string.Equals(
            value?.Trim(),
            MultilineDelimiter,
            StringComparison.Ordinal);
    }
}
