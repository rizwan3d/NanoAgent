using System.Text.Json;

namespace NanoAgent.Application.Tools;

internal static class ToolArguments
{
    public static bool TryGetString(
        JsonElement arguments,
        string propertyName,
        out string? value,
        bool trim = true)
    {
        if (arguments.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            if (trim)
            {
                value = value?.Trim();
            }

            return value is not null;
        }

        value = null;
        return false;
    }

    public static bool TryGetNonEmptyString(
        JsonElement arguments,
        string propertyName,
        out string? value,
        bool trim = true)
    {
        if (!TryGetString(arguments, propertyName, out value, trim))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(value);
    }

    public static string? GetOptionalString(
        JsonElement arguments,
        string propertyName)
    {
        return TryGetString(arguments, propertyName, out string? value)
            ? value
            : null;
    }

    public static bool GetBoolean(
        JsonElement arguments,
        string propertyName,
        bool defaultValue = false)
    {
        return TryGetBoolean(arguments, propertyName, out bool value)
            ? value
            : defaultValue;
    }

    public static bool TryGetBoolean(
        JsonElement arguments,
        string propertyName,
        out bool value)
    {
        if (arguments.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryGetInt32(
        JsonElement arguments,
        string propertyName,
        out int value)
    {
        if (arguments.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
