namespace NanoAgent.Application.Utilities;

internal static class MarkdownCodeBlock
{
    public static string Wrap(
        string content,
        string? infoString = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        string fence = CreateFence(content);
        string header = string.IsNullOrWhiteSpace(infoString)
            ? fence
            : fence + infoString.Trim();

        return $"{header}\n{content}\n{fence}";
    }

    private static string CreateFence(string content)
    {
        int longestRun = 0;
        int currentRun = 0;

        foreach (char character in content)
        {
            if (character == '`')
            {
                currentRun++;
                longestRun = Math.Max(longestRun, currentRun);
                continue;
            }

            currentRun = 0;
        }

        return new string('`', Math.Max(3, longestRun + 1));
    }
}
