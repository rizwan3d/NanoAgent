using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class OpenAiConversationResponseMapper : IConversationResponseMapper
{
    public ConversationResponse Map(ConversationProviderPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.ProviderKind == ProviderKind.OpenAiChatGptAccount)
        {
            return MapResponsesPayload(payload);
        }

        OpenAiChatCompletionResponse? response = JsonSerializer.Deserialize(
            payload.RawContent,
            OpenAiConversationJsonContext.Default.OpenAiChatCompletionResponse);

        OpenAiChatCompletionChoice? firstChoice = response?.Choices?.FirstOrDefault();
        OpenAiChatCompletionResponseMessage? message = firstChoice?.Message;
        string? responseId = response?.Id ?? payload.ResponseId;

        if (message is null)
        {
            throw new ConversationResponseException(
                "The provider response did not contain a chat completion message.");
        }

        List<ConversationToolCall> toolCalls = [];
        int toolCallOrdinal = 0;

        if (message.ToolCalls is not null)
        {
            foreach (OpenAiChatCompletionToolCall toolCall in message.ToolCalls)
            {
                if (!string.IsNullOrWhiteSpace(toolCall.Type) &&
                    !string.Equals(toolCall.Type, "function", StringComparison.Ordinal))
                {
                    continue;
                }

                string? functionName = NormalizeText(toolCall.Function?.Name);
                string? functionArguments = NormalizeText(toolCall.Function?.Arguments);
                if (functionName is null || functionArguments is null)
                {
                    throw new ConversationResponseException(
                        "The provider returned an incomplete tool call payload.");
                }

                toolCalls.Add(new ConversationToolCall(
                    CreateToolCallId(toolCall.Id, responseId, ++toolCallOrdinal),
                    functionName,
                    functionArguments));
            }
        }

        if (message.FunctionCall is not null)
        {
            string? functionName = NormalizeText(message.FunctionCall.Name);
            string? functionArguments = NormalizeText(message.FunctionCall.Arguments);
            if (functionName is null || functionArguments is null)
            {
                throw new ConversationResponseException(
                    "The provider returned an incomplete legacy function call payload.");
            }

            toolCalls.Add(new ConversationToolCall(
                CreateToolCallId("legacy_function_call", responseId, ++toolCallOrdinal),
                functionName,
                functionArguments));
        }

        string? assistantMessage = ExtractAssistantMessage(message);

        if (toolCalls.Count == 0 && ContainsRawToolCallMarkup(assistantMessage))
        {
            throw new ConversationResponseException(
                CreateRawToolCallMarkupMessage(firstChoice, responseId),
                isRetryableRawToolCallResponse: true);
        }

        if (toolCalls.Count == 0 && assistantMessage is null)
        {
            throw new ConversationResponseException(
                CreateEmptyResponseMessage(firstChoice, responseId),
                isRetryableEmptyResponse: IsRetryableEmptyStopResponse(firstChoice));
        }

        return new ConversationResponse(
            assistantMessage,
            toolCalls,
            responseId,
            response?.Usage?.CompletionTokens,
            response?.Usage?.PromptTokens,
            response?.Usage?.TotalTokens,
            response?.Usage?.PromptTokensDetails?.CachedTokens);
    }

    private static ConversationResponse MapResponsesPayload(ConversationProviderPayload payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload.RawContent);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("error", out JsonElement error) &&
            error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            throw new ConversationResponseException(
                $"The provider returned an error response: {ExtractResponsesError(error)}");
        }

        string? responseId = TryGetPropertyString(root, "id") ?? payload.ResponseId;
        List<string> contentParts = [];
        List<ConversationToolCall> toolCalls = [];
        int toolCallOrdinal = 0;

        if (root.TryGetProperty("output", out JsonElement output) &&
            output.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement outputItem in output.EnumerateArray())
            {
                ExtractResponsesOutputItem(
                    outputItem,
                    responseId,
                    contentParts,
                    toolCalls,
                    ref toolCallOrdinal);
            }
        }

        if (TryGetPropertyString(root, "output_text") is string outputText)
        {
            contentParts.Add(outputText);
        }
        else if (TryGetPropertyString(root, "text") is string text)
        {
            contentParts.Add(text);
        }

        string? assistantMessage = NormalizeText(string.Join(
            Environment.NewLine + Environment.NewLine,
            contentParts.Where(static content => !string.IsNullOrWhiteSpace(content))));

        if (toolCalls.Count == 0 && assistantMessage is null)
        {
            throw new ConversationResponseException(
                "The provider response did not contain assistant content or usable tool calls.");
        }

        TryGetResponsesUsage(
            root,
            out int? completionTokens,
            out int? promptTokens,
            out int? totalTokens,
            out int? cachedPromptTokens);

        return new ConversationResponse(
            assistantMessage,
            toolCalls,
            responseId,
            completionTokens,
            promptTokens,
            totalTokens,
            cachedPromptTokens);
    }

    private static void ExtractResponsesOutputItem(
        JsonElement outputItem,
        string? responseId,
        List<string> contentParts,
        List<ConversationToolCall> toolCalls,
        ref int toolCallOrdinal)
    {
        if (outputItem.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string? type = TryGetPropertyString(outputItem, "type");
        if (string.Equals(type, "message", StringComparison.Ordinal))
        {
            ExtractResponsesMessageContent(outputItem, contentParts);
            return;
        }

        if (string.Equals(type, "function_call", StringComparison.Ordinal) ||
            string.Equals(type, "tool_call", StringComparison.Ordinal))
        {
            string? functionName = TryGetPropertyString(outputItem, "name") ??
                TryGetNestedPropertyString(outputItem, "function", "name") ??
                TryGetPropertyString(outputItem, "function_name");
            string? functionArguments = TryGetPropertyString(outputItem, "arguments") ??
                TryGetNestedPropertyString(outputItem, "function", "arguments") ??
                TryGetRawProperty(outputItem, "input");

            if (functionName is null || functionArguments is null)
            {
                throw new ConversationResponseException(
                    "The provider returned an incomplete tool call payload.");
            }

            string? rawId = TryGetPropertyString(outputItem, "call_id") ??
                TryGetPropertyString(outputItem, "tool_call_id") ??
                TryGetPropertyString(outputItem, "id");

            toolCalls.Add(new ConversationToolCall(
                CreateToolCallId(rawId, responseId, ++toolCallOrdinal),
                functionName,
                functionArguments));
        }
    }

    private static void ExtractResponsesMessageContent(
        JsonElement outputItem,
        List<string> contentParts)
    {
        if (!outputItem.TryGetProperty("content", out JsonElement content))
        {
            return;
        }

        if (content.ValueKind == JsonValueKind.String &&
            NormalizeText(content.GetString()) is string text)
        {
            contentParts.Add(text);
            return;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            if (ExtractContentPartText(content) is string objectText)
            {
                contentParts.Add(objectText);
            }

            return;
        }

        foreach (JsonElement contentPart in content.EnumerateArray())
        {
            if (ExtractContentPartText(contentPart) is string partText)
            {
                contentParts.Add(partText);
            }
        }
    }

    private static void TryGetResponsesUsage(
        JsonElement root,
        out int? completionTokens,
        out int? promptTokens,
        out int? totalTokens,
        out int? cachedPromptTokens)
    {
        completionTokens = null;
        promptTokens = null;
        totalTokens = null;
        cachedPromptTokens = null;

        if (!root.TryGetProperty("usage", out JsonElement usage) ||
            usage.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        completionTokens = TryGetInt32(usage, "output_tokens") ??
            TryGetInt32(usage, "completion_tokens");
        promptTokens = TryGetInt32(usage, "input_tokens") ??
            TryGetInt32(usage, "prompt_tokens");
        totalTokens = TryGetInt32(usage, "total_tokens");
        cachedPromptTokens =
            TryGetNestedInt32(usage, "input_tokens_details", "cached_tokens") ??
            TryGetNestedInt32(usage, "prompt_tokens_details", "cached_tokens") ??
            TryGetInt32(usage, "cached_tokens") ??
            TryGetInt32(usage, "cache_read_input_tokens");
    }

    private static string ExtractResponsesError(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.String)
        {
            return NormalizeText(error.GetString()) ?? "Unknown error.";
        }

        if (error.ValueKind == JsonValueKind.Object)
        {
            return TryGetPropertyString(error, "message") ??
                TryGetPropertyString(error, "detail") ??
                TryGetPropertyString(error, "type") ??
                Truncate(error.GetRawText(), 200);
        }

        return Truncate(error.GetRawText(), 200);
    }

    private static string CreateEmptyResponseMessage(
        OpenAiChatCompletionChoice? choice,
        string? responseId)
    {
        string? finishReason = NormalizeText(choice?.FinishReason);
        string? idSuffix = NormalizeText(responseId) is string normalizedResponseId
            ? $" Response id: {normalizedResponseId}."
            : null;
        string? finishReasonSuffix = finishReason is null
            ? null
            : $" Finish reason: {finishReason}.";

        return "The provider returned neither assistant content, a refusal, nor usable tool calls." +
               finishReasonSuffix +
               idSuffix;
    }

    private static string CreateRawToolCallMarkupMessage(
        OpenAiChatCompletionChoice? choice,
        string? responseId)
    {
        string? finishReason = NormalizeText(choice?.FinishReason);
        string? idSuffix = NormalizeText(responseId) is string normalizedResponseId
            ? $" Response id: {normalizedResponseId}."
            : null;
        string? finishReasonSuffix = finishReason is null
            ? null
            : $" Finish reason: {finishReason}.";

        return "The provider returned raw tool-call markup in assistant content instead of a structured tool call." +
               finishReasonSuffix +
               idSuffix;
    }

    private static bool IsRetryableEmptyStopResponse(OpenAiChatCompletionChoice? choice)
    {
        return string.Equals(
            NormalizeText(choice?.FinishReason),
            "stop",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateToolCallId(string? rawId, string? responseId, int ordinal)
    {
        string? normalizedId = NormalizeText(rawId);
        if (normalizedId is not null)
        {
            return normalizedId;
        }

        string? normalizedResponseId = NormalizeText(responseId);
        return normalizedResponseId is null
            ? $"tool_call_{ordinal}"
            : $"{normalizedResponseId}_tool_call_{ordinal}";
    }

    private static string? ExtractAssistantMessage(OpenAiChatCompletionResponseMessage message)
    {
        string? content = ExtractContentText(message.Content);
        if (content is not null)
        {
            return content;
        }

        return NormalizeText(message.Refusal);
    }

    private static bool ContainsRawToolCallMarkup(string? assistantMessage)
    {
        if (assistantMessage is null)
        {
            return false;
        }

        return assistantMessage.Contains("<|channel>call:", StringComparison.Ordinal) ||
            assistantMessage.Contains("<tool_call|>", StringComparison.Ordinal);
    }

    private static string? TryGetPropertyString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out string? value)
            ? value
            : null;
    }

    private static string? TryGetNestedPropertyString(
        JsonElement element,
        string objectPropertyName,
        string propertyName)
    {
        if (!element.TryGetProperty(objectPropertyName, out JsonElement nestedObject) ||
            nestedObject.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetPropertyString(nestedObject, propertyName);
    }

    private static string? TryGetRawProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? NormalizeText(property.GetString())
            : property.GetRawText();
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out int value))
        {
            return value;
        }

        return null;
    }

    private static int? TryGetNestedInt32(
        JsonElement element,
        string objectPropertyName,
        string valuePropertyName)
    {
        if (!element.TryGetProperty(objectPropertyName, out JsonElement nestedObject) ||
            nestedObject.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetInt32(nestedObject, valuePropertyName);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string? ExtractContentText(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.Undefined => null,
            JsonValueKind.Null => null,
            JsonValueKind.String => NormalizeText(content.GetString()),
            JsonValueKind.Array => NormalizeText(string.Join(
                Environment.NewLine + Environment.NewLine,
                content.EnumerateArray()
                    .Select(ExtractContentPartText)
                    .Where(static text => !string.IsNullOrWhiteSpace(text)))),
            JsonValueKind.Object => ExtractContentPartText(content),
            _ => null
        };
    }

    private static string? ExtractContentPartText(JsonElement contentPart)
    {
        if (contentPart.ValueKind == JsonValueKind.String)
        {
            return NormalizeText(contentPart.GetString());
        }

        if (contentPart.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetString(contentPart, "text", out string? directText))
        {
            return directText;
        }

        if (contentPart.TryGetProperty("text", out JsonElement nestedText) &&
            nestedText.ValueKind == JsonValueKind.Object &&
            TryGetString(nestedText, "value", out string? nestedValue))
        {
            return nestedValue;
        }

        if (TryGetString(contentPart, "content", out string? fallbackContent))
        {
            return fallbackContent;
        }

        return null;
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;

        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = NormalizeText(property.GetString());
        return value is not null;
    }
}
