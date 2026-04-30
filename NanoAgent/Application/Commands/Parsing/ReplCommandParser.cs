using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class ReplCommandParser : IReplCommandParser
{
    public ParsedReplCommand Parse(string commandText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

        string trimmedInput = commandText.Trim();
        if (!trimmedInput.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "REPL commands must start with '/'.",
                nameof(commandText));
        }

        string commandBody = trimmedInput[1..].Trim();
        if (commandBody.Length == 0)
        {
            return new ParsedReplCommand(
                trimmedInput,
                string.Empty,
                string.Empty,
                []);
        }

        int firstSpaceIndex = commandBody.IndexOf(' ');
        if (firstSpaceIndex < 0)
        {
            return new ParsedReplCommand(
                trimmedInput,
                commandBody,
                string.Empty,
                []);
        }

        string commandName = commandBody[..firstSpaceIndex];
        string argumentText = commandBody[(firstSpaceIndex + 1)..].Trim();
        string[] arguments = argumentText.Length == 0
            ? []
            : argumentText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ParsedReplCommand(
            trimmedInput,
            commandName,
            argumentText,
            arguments);
    }
}
