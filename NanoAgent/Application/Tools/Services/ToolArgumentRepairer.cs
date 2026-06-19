using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NanoAgent.Application.Tools.Services;

internal static class ToolArgumentRepairer
{
    private static readonly Regex MarkdownAutoLinkPattern = new(
        @"\[(?<label>[^\[\]\r\n]+)\]\((?<destination>[^()\r\n]+)\)",
        RegexOptions.Compiled);

    public static JsonElement RepairIfNeeded(
        JsonElement arguments,
        string schemaJson,
        ReplSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ShouldRepair(session))
        {
            return arguments;
        }

        using JsonDocument schemaDocument = JsonDocument.Parse(schemaJson);
        if (schemaDocument.RootElement.ValueKind != JsonValueKind.Object)
        {
            return arguments;
        }

        JsonNode? parsedNode = JsonNode.Parse(arguments.GetRawText());
        if (parsedNode is not JsonObject argumentsObject)
        {
            return arguments;
        }

        bool repaired = RepairObject(argumentsObject, schemaDocument.RootElement);
        if (!repaired)
        {
            return arguments;
        }

        using JsonDocument repairedDocument = JsonDocument.Parse(argumentsObject.ToJsonString());
        return repairedDocument.RootElement.Clone();
    }

    private static bool ShouldRepair(ReplSessionContext session)
    {
        return session.ProviderProfile.ProviderKind == ProviderKind.DeepSeek ||
               session.ActiveModelId.Contains("deepseek", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RepairObject(
        JsonObject argumentsObject,
        JsonElement schema)
    {
        bool changed = false;
        Dictionary<string, JsonElement> propertySchemas = GetPropertySchemas(schema);
        HashSet<string> requiredProperties = GetRequiredProperties(schema);
        bool disallowAdditionalProperties = schema.TryGetProperty("additionalProperties", out JsonElement additionalPropertiesElement) &&
                                            additionalPropertiesElement.ValueKind == JsonValueKind.False;

        if (disallowAdditionalProperties)
        {
            foreach (string propertyName in argumentsObject.Select(static pair => pair.Key).ToArray())
            {
                if (propertySchemas.ContainsKey(propertyName))
                {
                    continue;
                }

                if (argumentsObject[propertyName] is null)
                {
                    argumentsObject.Remove(propertyName);
                    changed = true;
                }
            }
        }

        foreach ((string propertyName, JsonElement propertySchema) in propertySchemas)
        {
            if (!argumentsObject.TryGetPropertyValue(propertyName, out JsonNode? propertyNode))
            {
                continue;
            }

            bool isRequired = requiredProperties.Contains(propertyName);
            if (propertyNode is null)
            {
                if (!isRequired)
                {
                    argumentsObject.Remove(propertyName);
                    changed = true;
                }

                continue;
            }

            changed |= RepairProperty(argumentsObject, propertyName, propertyNode, propertySchema);
        }

        return changed;
    }

    private static bool RepairProperty(
        JsonObject parentObject,
        string propertyName,
        JsonNode propertyNode,
        JsonElement propertySchema)
    {
        bool changed = false;
        string? expectedType = GetExpectedType(propertySchema);

        if (string.Equals(expectedType, "array", StringComparison.Ordinal))
        {
            if (TryRepairArrayNode(propertyNode, propertySchema, out JsonNode? repairedArrayNode))
            {
                parentObject[propertyName] = repairedArrayNode;
                propertyNode = repairedArrayNode!;
                changed = true;
            }

            if (propertyNode is JsonArray arrayNode &&
                TryGetItemsSchema(propertySchema, out JsonElement itemSchema))
            {
                changed |= RepairArrayItems(arrayNode, itemSchema);
            }

            return changed;
        }

        if (string.Equals(expectedType, "object", StringComparison.Ordinal) &&
            propertyNode is JsonObject objectNode)
        {
            return RepairObject(objectNode, propertySchema);
        }

        if (string.Equals(expectedType, "string", StringComparison.Ordinal) &&
            IsPathLikeProperty(propertyName) &&
            TryGetStringValue(propertyNode, out string? pathValue) &&
            TryUnwrapDegenerateMarkdownAutoLinks(pathValue!, out string repairedPath))
        {
            parentObject[propertyName] = repairedPath;
            return true;
        }

        return false;
    }

    private static bool RepairArrayItems(
        JsonArray arrayNode,
        JsonElement itemSchema)
    {
        bool changed = false;
        string? itemType = GetExpectedType(itemSchema);

        if (!string.Equals(itemType, "object", StringComparison.Ordinal))
        {
            return false;
        }

        for (int index = 0; index < arrayNode.Count; index++)
        {
            if (arrayNode[index] is JsonObject objectItem)
            {
                changed |= RepairObject(objectItem, itemSchema);
            }
        }

        return changed;
    }

    private static bool TryRepairArrayNode(
        JsonNode propertyNode,
        JsonElement propertySchema,
        out JsonNode? repairedNode)
    {
        repairedNode = null;

        if (TryGetStringValue(propertyNode, out string? stringValue))
        {
            if (TryParseStringifiedArray(stringValue!, out JsonArray? parsedArray))
            {
                repairedNode = parsedArray;
                return true;
            }

            repairedNode = new JsonArray(stringValue);
            return true;
        }

        if (propertyNode is not JsonObject objectNode)
        {
            return false;
        }

        if (objectNode.Count == 0)
        {
            repairedNode = new JsonArray();
            return true;
        }

        if (ItemsExpectObjects(propertySchema))
        {
            repairedNode = new JsonArray(objectNode.DeepClone());
            return true;
        }

        return false;
    }

    private static bool TryParseStringifiedArray(
        string value,
        out JsonArray? array)
    {
        array = null;
        string trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']')
        {
            return false;
        }

        try
        {
            JsonNode? parsedNode = JsonNode.Parse(trimmed);
            if (parsedNode is not JsonArray parsedArray)
            {
                return false;
            }

            array = parsedArray;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Dictionary<string, JsonElement> GetPropertySchemas(JsonElement schema)
    {
        Dictionary<string, JsonElement> properties = new(StringComparer.Ordinal);
        if (!schema.TryGetProperty("properties", out JsonElement propertiesElement) ||
            propertiesElement.ValueKind != JsonValueKind.Object)
        {
            return properties;
        }

        foreach (JsonProperty property in propertiesElement.EnumerateObject())
        {
            properties[property.Name] = property.Value.Clone();
        }

        return properties;
    }

    private static HashSet<string> GetRequiredProperties(JsonElement schema)
    {
        HashSet<string> requiredProperties = new(StringComparer.Ordinal);
        if (!schema.TryGetProperty("required", out JsonElement requiredElement) ||
            requiredElement.ValueKind != JsonValueKind.Array)
        {
            return requiredProperties;
        }

        foreach (JsonElement item in requiredElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(item.GetString()))
            {
                requiredProperties.Add(item.GetString()!);
            }
        }

        return requiredProperties;
    }

    private static bool TryGetItemsSchema(
        JsonElement propertySchema,
        out JsonElement itemSchema)
    {
        if (propertySchema.TryGetProperty("items", out JsonElement itemsElement))
        {
            itemSchema = itemsElement.Clone();
            return true;
        }

        itemSchema = default;
        return false;
    }

    private static bool ItemsExpectObjects(JsonElement propertySchema)
    {
        return TryGetItemsSchema(propertySchema, out JsonElement itemSchema) &&
               string.Equals(GetExpectedType(itemSchema), "object", StringComparison.Ordinal);
    }

    private static string? GetExpectedType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out JsonElement typeElement))
        {
            return null;
        }

        if (typeElement.ValueKind == JsonValueKind.String)
        {
            return typeElement.GetString();
        }

        if (typeElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement typeName in typeElement.EnumerateArray())
        {
            if (typeName.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? candidate = typeName.GetString();
            if (!string.IsNullOrWhiteSpace(candidate) &&
                !string.Equals(candidate, "null", StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryGetStringValue(
        JsonNode propertyNode,
        out string? value)
    {
        value = null;
        return propertyNode is JsonValue jsonValue &&
               jsonValue.TryGetValue(out value) &&
               value is not null;
    }

    private static bool IsPathLikeProperty(string propertyName)
    {
        return string.Equals(propertyName, "path", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(propertyName, "workingDirectory", StringComparison.OrdinalIgnoreCase) ||
               propertyName.EndsWith("Path", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryUnwrapDegenerateMarkdownAutoLinks(
        string value,
        out string repairedValue)
    {
        repairedValue = MarkdownAutoLinkPattern.Replace(
            value,
            static match =>
            {
                string label = match.Groups["label"].Value.Trim();
                string destination = NormalizeMarkdownAutoLinkDestination(match.Groups["destination"].Value);

                return string.Equals(label, destination, StringComparison.OrdinalIgnoreCase)
                    ? destination
                    : match.Value;
            });

        return !string.Equals(value, repairedValue, StringComparison.Ordinal);
    }

    private static string NormalizeMarkdownAutoLinkDestination(string destination)
    {
        string trimmed = destination.Trim();
        return Regex.Replace(
            trimmed,
            @"^[A-Za-z][A-Za-z0-9+.-]*://",
            string.Empty,
            RegexOptions.None,
            TimeSpan.FromMilliseconds(100));
    }
}
