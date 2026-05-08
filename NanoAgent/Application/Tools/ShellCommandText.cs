using System.Text.RegularExpressions;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Tools;

internal enum ShellCommandSegmentCondition
{
    Always,
    OnSuccess,
    OnFailure
}

internal readonly record struct ShellCommandSegment(
    ShellCommandSegmentCondition Condition,
    string CommandText);

internal static class ShellCommandText
{
    public static bool ContainsControlSyntax(string commandText)
    {
        commandText = NormalizeCommandText(commandText);

        return commandText.Contains('|') ||
               commandText.Contains(';') ||
               commandText.Contains("&&", StringComparison.Ordinal) ||
               commandText.Contains("||", StringComparison.Ordinal) ||
               commandText.Contains('>') ||
               commandText.Contains('<') ||
               commandText.Contains("$(", StringComparison.Ordinal) ||
               commandText.Contains('`');
    }

    public static IReadOnlyList<ShellCommandSegment> ParseSegments(string commandText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

        commandText = NormalizeCommandText(commandText);
        List<ShellCommandSegment> segments = [];
        int segmentStart = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        ShellCommandSegmentCondition nextCondition = ShellCommandSegmentCondition.Always;

        for (int index = 0; index < commandText.Length; index++)
        {
            char current = commandText[index];

            if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (current == '&' &&
                index + 1 < commandText.Length &&
                commandText[index + 1] == '&')
            {
                if (TryAddSegment(segmentStart, index, nextCondition))
                {
                    nextCondition = ShellCommandSegmentCondition.OnSuccess;
                }

                index++;
                segmentStart = index + 1;
                continue;
            }

            if (current == '|' &&
                index + 1 < commandText.Length &&
                commandText[index + 1] == '|')
            {
                if (TryAddSegment(segmentStart, index, nextCondition))
                {
                    nextCondition = ShellCommandSegmentCondition.OnFailure;
                }

                index++;
                segmentStart = index + 1;
                continue;
            }

            if (current == ';')
            {
                if (TryAddSegment(segmentStart, index, nextCondition))
                {
                    nextCondition = ShellCommandSegmentCondition.Always;
                }

                segmentStart = index + 1;
                continue;
            }

            if (current == '\r' || current == '\n')
            {
                if (TryAddSegment(segmentStart, index, nextCondition))
                {
                    nextCondition = ShellCommandSegmentCondition.Always;
                }

                if (current == '\r' &&
                    index + 1 < commandText.Length &&
                    commandText[index + 1] == '\n')
                {
                    index++;
                }

                segmentStart = index + 1;
            }
        }

        TryAddSegment(segmentStart, commandText.Length, nextCondition);
        return segments;

        bool TryAddSegment(
            int start,
            int endExclusive,
            ShellCommandSegmentCondition condition)
        {
            string segmentText = commandText[start..endExclusive].Trim();
            if (string.IsNullOrWhiteSpace(segmentText))
            {
                return false;
            }

            segments.Add(new ShellCommandSegment(condition, segmentText));
            return true;
        }
    }

    public static bool TryGetCommandName(
        string commandText,
        out string commandName)
    {
        string[] tokens = Tokenize(commandText);
        commandName = tokens.Length == 0
            ? string.Empty
            : NormalizeCommandToken(tokens[0]);

        return !string.IsNullOrWhiteSpace(commandName);
    }

    public static string[] Tokenize(string commandText)
    {
        commandText = NormalizeCommandText(commandText);

        MatchCollection matches = Regex.Matches(
            commandText,
            "\"[^\"]*\"|'[^']*'|\\S+");

        if (matches.Count == 0)
        {
            return [];
        }

        List<string> tokens = new(matches.Count);
        foreach (Match match in matches)
        {
            string value = match.Value.Trim();
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                tokens.Add(value);
            }
        }

        return tokens.ToArray();
    }

    public static string NormalizeCommandToken(string token)
    {
        token = NormalizeCommandText(token);
        string trimmedToken = token.Trim();
        if (string.IsNullOrWhiteSpace(trimmedToken))
        {
            return string.Empty;
        }

        string fileName = Path.GetFileName(trimmedToken.Replace('/', Path.DirectorySeparatorChar));
        string normalizedCommand = Path.GetFileNameWithoutExtension(fileName);
        return normalizedCommand.Any(static ch => char.IsLetterOrDigit(ch))
            ? normalizedCommand
            : string.Empty;
    }

    public static string NormalizeCommandText(string commandText)
    {
        ArgumentNullException.ThrowIfNull(commandText);

        return SuspiciousUnicodeText.NormalizeForCommand(commandText);
    }
}
