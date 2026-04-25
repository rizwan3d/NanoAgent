using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Conversation;

internal static class OpenAiResponsesEventStreamParser
{
    public static string? TryParseResponsePayload(string responseBody)
    {
        if (!LooksLikeEventStream(responseBody))
        {
            return null;
        }

        string? completedResponsePayload = null;
        string? errorPayload = null;
        string? responseId = null;
        string? usagePayload = null;
        string? completedText = null;
        StringBuilder textDeltas = new();
        SortedDictionary<int, string> outputItems = [];

        foreach (string data in EnumerateEventData(responseBody))
        {
            string trimmedData = data.Trim();
            if (trimmedData.Length == 0 || string.Equals(trimmedData, "[DONE]", StringComparison.Ordinal))
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(trimmedData);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (root.TryGetProperty("error", out JsonElement error) &&
                error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                errorPayload = BuildErrorPayload(error.GetRawText());
            }

            string? eventType = TryGetString(root, "type");
            if (root.TryGetProperty("response", out JsonElement response) &&
                response.ValueKind == JsonValueKind.Object)
            {
                responseId ??= TryGetString(response, "id");
                if (response.TryGetProperty("usage", out JsonElement responseUsage) &&
                    responseUsage.ValueKind == JsonValueKind.Object)
                {
                    usagePayload = responseUsage.GetRawText();
                }

                if (response.TryGetProperty("error", out JsonElement responseError) &&
                    responseError.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                {
                    errorPayload = BuildErrorPayload(responseError.GetRawText());
                }

                string responsePayload = response.GetRawText();
                if (string.Equals(eventType, "response.completed", StringComparison.Ordinal) ||
                    string.Equals(eventType, "response.failed", StringComparison.Ordinal))
                {
                    if (HasUsableResponsesOutput(response))
                    {
                        completedResponsePayload = responsePayload;
                    }
                }
            }
            else if (root.TryGetProperty("output", out JsonElement output) &&
                output.ValueKind == JsonValueKind.Array)
            {
                if (HasUsableResponsesOutput(root))
                {
                    completedResponsePayload = root.GetRawText();
                }
            }

            if (TryGetString(root, "response_id") is string eventResponseId)
            {
                responseId ??= eventResponseId;
            }

            if (root.TryGetProperty("item", out JsonElement item) &&
                item.ValueKind == JsonValueKind.Object &&
                string.Equals(eventType, "response.output_item.done", StringComparison.Ordinal))
            {
                outputItems[TryGetOutputIndex(root, outputItems.Count)] = item.GetRawText();
            }

            if (string.Equals(eventType, "response.output_text.delta", StringComparison.Ordinal) &&
                TryGetStringPreservingWhitespace(root, "delta") is string delta)
            {
                textDeltas.Append(delta);
            }

            if (string.Equals(eventType, "response.output_text.done", StringComparison.Ordinal) &&
                TryGetStringPreservingWhitespace(root, "text") is string text)
            {
                completedText = text;
            }
        }

        if (completedResponsePayload is not null)
        {
            return completedResponsePayload;
        }

        if (errorPayload is not null)
        {
            return errorPayload;
        }

        List<string> usableOutputItems = outputItems.Values
            .Where(HasUsableOutputItemPayload)
            .ToList();
        if (usableOutputItems.Count > 0)
        {
            return BuildResponsesPayload(
                responseId,
                usableOutputItems,
                usagePayload);
        }

        string outputText = completedText ?? textDeltas.ToString();
        return string.IsNullOrWhiteSpace(outputText)
            ? null
            : BuildResponsesPayload(
                responseId,
                [BuildMessageOutputItem(outputText)],
                usagePayload);
    }

    private static bool LooksLikeEventStream(string responseBody)
    {
        string trimmed = responseBody.TrimStart();
        return trimmed.StartsWith("event:", StringComparison.Ordinal) ||
            trimmed.StartsWith("data:", StringComparison.Ordinal) ||
            responseBody.Contains("\ndata:", StringComparison.Ordinal) ||
            responseBody.Contains("\r\ndata:", StringComparison.Ordinal);
    }

    private static bool HasUsableResponsesOutput(JsonElement response)
    {
        return TryGetString(response, "output_text") is not null ||
            TryGetString(response, "text") is not null ||
            response.TryGetProperty("output", out JsonElement output) &&
            output.ValueKind == JsonValueKind.Array &&
            output.EnumerateArray().Any(HasUsableOutputItem);
    }

    private static bool HasUsableOutputItemPayload(string outputItemPayload)
    {
        using JsonDocument document = JsonDocument.Parse(outputItemPayload);
        return HasUsableOutputItem(document.RootElement);
    }

    private static bool HasUsableOutputItem(JsonElement outputItem)
    {
        if (outputItem.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? type = TryGetString(outputItem, "type");
        if (string.Equals(type, "function_call", StringComparison.Ordinal) ||
            string.Equals(type, "tool_call", StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(type, "message", StringComparison.Ordinal) ||
            !outputItem.TryGetProperty("content", out JsonElement content))
        {
            return false;
        }

        return HasUsableMessageContent(content);
    }

    private static bool HasUsableMessageContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return !string.IsNullOrWhiteSpace(content.GetString());
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            return content.EnumerateArray().Any(HasUsableContentPart);
        }

        return HasUsableContentPart(content);
    }

    private static bool HasUsableContentPart(JsonElement contentPart)
    {
        if (contentPart.ValueKind == JsonValueKind.String)
        {
            return !string.IsNullOrWhiteSpace(contentPart.GetString());
        }

        if (contentPart.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryGetString(contentPart, "text") is not null ||
            TryGetString(contentPart, "content") is not null)
        {
            return true;
        }

        return contentPart.TryGetProperty("text", out JsonElement nestedText) &&
            nestedText.ValueKind == JsonValueKind.Object &&
            TryGetString(nestedText, "value") is not null;
    }

    private static IEnumerable<string> EnumerateEventData(string responseBody)
    {
        using StringReader reader = new(responseBody);
        StringBuilder data = new();
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return data.ToString();
                    data.Clear();
                }

                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            if (data.Length > 0)
            {
                data.Append('\n');
            }

            string value = line[5..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            data.Append(value);
        }

        if (data.Length > 0)
        {
            yield return data.ToString();
        }
    }

    private static string BuildErrorPayload(string errorJson)
    {
        return $$"""{"error":{{errorJson}}}""";
    }

    private static string BuildResponsesPayload(
        string? responseId,
        IEnumerable<string> outputItemPayloads,
        string? usagePayload)
    {
        StringBuilder response = new();
        response.Append('{');
        bool hasProperty = false;

        if (!string.IsNullOrWhiteSpace(responseId))
        {
            response.Append("\"id\":\"");
            response.Append(JsonEncodedText.Encode(responseId).ToString());
            response.Append('"');
            hasProperty = true;
        }

        AppendPropertySeparator(response, ref hasProperty);
        response.Append("\"output\":[");
        bool hasOutputItem = false;
        foreach (string outputItemPayload in outputItemPayloads)
        {
            if (hasOutputItem)
            {
                response.Append(',');
            }

            response.Append(outputItemPayload);
            hasOutputItem = true;
        }

        response.Append(']');

        if (!string.IsNullOrWhiteSpace(usagePayload))
        {
            AppendPropertySeparator(response, ref hasProperty);
            response.Append("\"usage\":");
            response.Append(usagePayload);
        }

        response.Append('}');
        return response.ToString();
    }

    private static string BuildMessageOutputItem(string text)
    {
        return $$"""{"type":"message","content":[{"type":"output_text","text":"{{JsonEncodedText.Encode(text)}}"}]}""";
    }

    private static void AppendPropertySeparator(StringBuilder builder, ref bool hasProperty)
    {
        if (hasProperty)
        {
            builder.Append(',');
            return;
        }

        hasProperty = true;
    }

    private static int TryGetOutputIndex(JsonElement element, int fallback)
    {
        if (element.TryGetProperty("output_index", out JsonElement property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out int outputIndex))
        {
            return outputIndex;
        }

        return fallback;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? value = property.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? TryGetStringPreservingWhitespace(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }
}
