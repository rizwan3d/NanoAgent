using System.Globalization;
using System.Text;

namespace NanoAgent.Application.Utilities;

public static class SuspiciousUnicodeText
{
    public static string NormalizeForCommand(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Normalize(NormalizationForm.FormKC);
    }

    public static string RenderVisible(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string normalized = value.Normalize(NormalizationForm.FormKC);
        StringBuilder builder = new(normalized.Length);
        foreach (Rune rune in normalized.EnumerateRunes())
        {
            if (IsSuspicious(rune))
            {
                AppendVisibleCodePoint(builder, rune);
                continue;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static bool IsSuspicious(Rune rune)
    {
        int value = rune.Value;
        if (value is '\t' or '\n' or '\r')
        {
            return false;
        }

        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.Control or
            UnicodeCategory.Format or
            UnicodeCategory.PrivateUse or
            UnicodeCategory.Surrogate or
            UnicodeCategory.OtherNotAssigned)
        {
            return true;
        }

        return value is >= 0xFE00 and <= 0xFE0F or
            >= 0xE0100 and <= 0xE01EF;
    }

    private static void AppendVisibleCodePoint(
        StringBuilder builder,
        Rune rune)
    {
        builder
            .Append("<U+")
            .Append(rune.Value.ToString("X4", CultureInfo.InvariantCulture));

        string name = GetKnownName(rune.Value);
        if (!string.IsNullOrWhiteSpace(name))
        {
            builder.Append(' ').Append(name);
        }

        builder.Append('>');
    }

    private static string GetKnownName(int value)
    {
        return value switch
        {
            0x0000 => "NULL",
            0x0008 => "BACKSPACE",
            0x001B => "ESCAPE",
            0x007F => "DELETE",
            0x00AD => "SOFT HYPHEN",
            0x061C => "ARABIC LETTER MARK",
            0x180E => "MONGOLIAN VOWEL SEPARATOR",
            0x200B => "ZERO WIDTH SPACE",
            0x200C => "ZERO WIDTH NON-JOINER",
            0x200D => "ZERO WIDTH JOINER",
            0x200E => "LEFT-TO-RIGHT MARK",
            0x200F => "RIGHT-TO-LEFT MARK",
            0x202A => "LEFT-TO-RIGHT EMBEDDING",
            0x202B => "RIGHT-TO-LEFT EMBEDDING",
            0x202C => "POP DIRECTIONAL FORMATTING",
            0x202D => "LEFT-TO-RIGHT OVERRIDE",
            0x202E => "RIGHT-TO-LEFT OVERRIDE",
            0x2060 => "WORD JOINER",
            0x2061 => "FUNCTION APPLICATION",
            0x2062 => "INVISIBLE TIMES",
            0x2063 => "INVISIBLE SEPARATOR",
            0x2064 => "INVISIBLE PLUS",
            0x2066 => "LEFT-TO-RIGHT ISOLATE",
            0x2067 => "RIGHT-TO-LEFT ISOLATE",
            0x2068 => "FIRST STRONG ISOLATE",
            0x2069 => "POP DIRECTIONAL ISOLATE",
            0xFEFF => "ZERO WIDTH NO-BREAK SPACE",
            >= 0xFE00 and <= 0xFE0F => "VARIATION SELECTOR",
            >= 0xE0100 and <= 0xE01EF => "VARIATION SELECTOR",
            _ => string.Empty
        };
    }
}
