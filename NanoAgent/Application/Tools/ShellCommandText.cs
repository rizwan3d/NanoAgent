using System.Text.RegularExpressions;

namespace NanoAgent.Application.Tools;

internal static class ShellCommandText
{
    public static bool ContainsControlSyntax(string commandText)
    {
        return commandText.Contains('|') ||
               commandText.Contains(';') ||
               commandText.Contains("&&", StringComparison.Ordinal) ||
               commandText.Contains("||", StringComparison.Ordinal) ||
               commandText.Contains('>') ||
               commandText.Contains('<') ||
               commandText.Contains("$(", StringComparison.Ordinal) ||
               commandText.Contains('`');
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
        string trimmedToken = token.Trim();
        if (string.IsNullOrWhiteSpace(trimmedToken))
        {
            return string.Empty;
        }

        string fileName = Path.GetFileName(trimmedToken.Replace('/', Path.DirectorySeparatorChar));
        return Path.GetFileNameWithoutExtension(fileName);
    }
}
