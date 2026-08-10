namespace StemCode.CLI;

public static class StringExtensions
{
    public static string CapitalizeFirstOnly(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(input[0]) + input[1..].ToLowerInvariant();
    }
}
