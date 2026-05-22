namespace NanoAgent.Infrastructure.Secrets;

internal static class ProcessEnvironment
{
    public static bool ShouldInclude(string key)
    {
        return !string.IsNullOrWhiteSpace(key);
    }

    public static void ValidateVariable(string key, string value)
    {
        if (key.Contains('\0'))
        {
            throw new ArgumentException(
                $"Environment variable name '{RenderEnvironmentName(key)}' must not contain embedded null characters.",
                nameof(key));
        }

        if (value.Contains('\0'))
        {
            throw new ArgumentException(
                $"Environment variable '{RenderEnvironmentName(key)}' must not contain embedded null characters.",
                nameof(value));
        }
    }

    private static string RenderEnvironmentName(string key)
    {
        int nullIndex = key.IndexOf('\0');
        return nullIndex >= 0
            ? key[..nullIndex] + "\\0..."
            : key;
    }
}
