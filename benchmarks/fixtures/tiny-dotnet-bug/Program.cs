using System;

string value = "OPENAI_API_KEY\0shadow";
string sanitized = EnvironmentValueSanitizer.TrimAtNullByte(value);

Console.WriteLine(sanitized == "OPENAI_API_KEY" ? "OK" : $"FAIL:{sanitized}");

internal static class EnvironmentValueSanitizer
{
    public static string TrimAtNullByte(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int nullIndex = value.IndexOf('\0');
        if (nullIndex < 0)
        {
            return value;
        }

        return value[(nullIndex + 1)..];
    }
}
