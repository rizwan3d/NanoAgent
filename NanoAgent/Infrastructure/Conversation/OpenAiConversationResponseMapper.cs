using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class OpenAiConversationResponseMapper : IConversationResponseMapper
{
    public ConversationResponse Map(ConversationProviderPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

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
            response?.Usage?.CompletionTokens);
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
