using NanoAgent.Application.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class ConversationProviderRequestPayloadFactory
{
    private const int AnthropicClaudeAccountMaxTokens = 8192;

    public OpenAiChatCompletionRequest BuildChatCompletionRequest(ConversationProviderRequest request)
    {
        List<OpenAiChatCompletionRequestMessage> messages = [];

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new OpenAiChatCompletionRequestMessage(
                "system",
                CreateStringContentElement(request.SystemPrompt.Trim())));
        }

        foreach (ConversationRequestMessage message in request.Messages)
        {
            messages.Add(MapChatCompletionMessage(message));
        }

        OpenAiChatCompletionToolDefinition[] tools = request.AvailableTools
            .Select(definition => new OpenAiChatCompletionToolDefinition(
                "function",
                new OpenAiChatCompletionFunctionDefinition(
                    definition.Name,
                    definition.Description,
                    definition.Schema)))
            .ToArray();

        return new OpenAiChatCompletionRequest(
            request.ModelId,
            messages,
            tools,
            ReasoningEffortOptions.ToProviderValue(request.ReasoningEffort));
    }

    public OpenAiResponsesRequest BuildResponsesRequest(ConversationProviderRequest request)
    {
        List<OpenAiResponsesInputItem> input = [];

        foreach (ConversationRequestMessage message in request.Messages)
        {
            input.AddRange(MapResponsesMessage(message));
        }

        OpenAiResponsesToolDefinition[] tools = request.AvailableTools
            .Select(definition => new OpenAiResponsesToolDefinition(
                "function",
                definition.Name,
                definition.Description,
                NormalizeResponsesToolSchema(definition.Schema),
                Strict: true))
            .ToArray();

        string? reasoningEffort = ReasoningEffortOptions.ToProviderValue(request.ReasoningEffort);

        return new OpenAiResponsesRequest(
            request.ModelId,
            input,
            Stream: true,
            Store: false,
            string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt.Trim(),
            tools.Length == 0 ? null : tools,
            reasoningEffort is null ? null : new OpenAiResponsesReasoning(reasoningEffort, "auto"),
            reasoningEffort is null ? null : ["reasoning.encrypted_content"],
            ParallelToolCalls: true);
    }

    public AnthropicMessagesRequest BuildAnthropicMessagesRequest(ConversationProviderRequest request)
    {
        List<AnthropicMessage> messages = [];

        for (int i = 0; i < request.Messages.Count; i++)
        {
            ConversationRequestMessage message = request.Messages[i];
            if (string.Equals(message.Role, "tool", StringComparison.Ordinal))
            {
                List<AnthropicContentBlock> toolResultBlocks = [];
                int toolMessageIndex = i;
                while (toolMessageIndex < request.Messages.Count &&
                    string.Equals(request.Messages[toolMessageIndex].Role, "tool", StringComparison.Ordinal))
                {
                    ConversationRequestMessage toolMessage = request.Messages[toolMessageIndex];
                    toolResultBlocks.Add(new AnthropicContentBlock(
                        "tool_result",
                        ToolUseId: toolMessage.ToolCallId,
                        Content: toolMessage.Content ?? string.Empty,
                        IsError: false));
                    toolMessageIndex++;
                }

                messages.Add(new AnthropicMessage("user", toolResultBlocks));
                i = toolMessageIndex - 1;
                continue;
            }

            if (string.Equals(message.Role, "assistant", StringComparison.Ordinal))
            {
                IReadOnlyList<AnthropicContentBlock> assistantContent = CreateAnthropicAssistantContent(message);
                if (assistantContent.Count > 0)
                {
                    messages.Add(new AnthropicMessage("assistant", assistantContent));
                }

                continue;
            }

            messages.Add(new AnthropicMessage("user", CreateAnthropicUserContent(message)));
        }

        List<AnthropicContentBlock> system = [];
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            system.Add(new AnthropicContentBlock("text", Text: request.SystemPrompt.Trim()));
        }

        AnthropicToolDefinition[] tools = request.AvailableTools
            .Select(definition => new AnthropicToolDefinition(
                definition.Name,
                definition.Description,
                definition.Schema))
            .ToArray();

        AnthropicThinking? thinking = ReasoningEffortOptions.ToProviderValue(request.ReasoningEffort) is null
            ? null
            : new AnthropicThinking("enabled", BudgetTokens: 1024);

        return new AnthropicMessagesRequest(
            request.ModelId,
            messages,
            AnthropicClaudeAccountMaxTokens,
            system,
            tools.Length == 0 ? null : tools,
            thinking);
    }

    private static IReadOnlyList<AnthropicContentBlock> CreateAnthropicUserContent(
        ConversationRequestMessage message)
    {
        List<AnthropicContentBlock> parts = [];
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            parts.Add(new AnthropicContentBlock("text", Text: message.Content));
        }

        foreach (ConversationAttachment attachment in message.Attachments)
        {
            if (attachment.IsImage)
            {
                parts.Add(new AnthropicContentBlock(
                    "image",
                    Source: new AnthropicImageSource(
                        "base64",
                        attachment.MediaType,
                        attachment.ContentBase64)));
                continue;
            }

            parts.Add(new AnthropicContentBlock(
                "text",
                Text: FormatAttachmentText(attachment)));
        }

        return parts;
    }

    private static IReadOnlyList<AnthropicContentBlock> CreateAnthropicAssistantContent(
        ConversationRequestMessage message)
    {
        List<AnthropicContentBlock> parts = [];
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            parts.Add(new AnthropicContentBlock("text", Text: message.Content));
        }

        foreach (ConversationToolCall toolCall in message.ToolCalls)
        {
            parts.Add(new AnthropicContentBlock(
                "tool_use",
                Id: NormalizeAnthropicToolCallId(toolCall.Id),
                Name: toolCall.Name,
                Input: ParseToolCallArguments(toolCall.ArgumentsJson)));
        }

        return parts;
    }

    private static JsonElement ParseToolCallArguments(string argumentsJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            string serializedInput = JsonSerializer.Serialize(
                argumentsJson,
                OpenAiConversationJsonContext.Default.String);
            using JsonDocument document = JsonDocument.Parse($$"""{"input":{{serializedInput}}}""");
            return document.RootElement.Clone();
        }
    }

    private static string NormalizeAnthropicToolCallId(string id)
    {
        string normalized = new(id
            .Select(static character => char.IsLetterOrDigit(character) ||
                    character is '_' or '-'
                ? character
                : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized)
            ? "tool_call"
            : normalized[..Math.Min(normalized.Length, 64)];
    }

    private static IReadOnlyList<OpenAiResponsesInputItem> MapResponsesMessage(
        ConversationRequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        List<OpenAiResponsesInputItem> items = [];

        if (string.Equals(message.Role, "tool", StringComparison.Ordinal))
        {
            items.Add(new OpenAiResponsesInputItem(
                Type: "function_call_output",
                CallId: message.ToolCallId,
                Output: message.Content));
            return items;
        }

        string contentType = string.Equals(message.Role, "assistant", StringComparison.Ordinal)
            ? "output_text"
            : "input_text";

        if (!string.IsNullOrWhiteSpace(message.Content) || message.Attachments.Count > 0)
        {
            items.Add(new OpenAiResponsesInputItem(
                Role: message.Role,
                Content: CreateResponsesContentParts(contentType, message)));
        }

        foreach (ConversationToolCall toolCall in message.ToolCalls)
        {
            items.Add(new OpenAiResponsesInputItem(
                Type: "function_call",
                CallId: toolCall.Id,
                Name: toolCall.Name,
                Arguments: toolCall.ArgumentsJson));
        }

        return items;
    }

    private static JsonElement NormalizeResponsesToolSchema(JsonElement schema)
    {
        JsonNode? node = JsonNode.Parse(schema.GetRawText());
        if (node is null)
        {
            return schema.Clone();
        }

        NormalizeSchemaNode(node);
        using JsonDocument document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static void NormalizeSchemaNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            if (TryGetString(jsonObject["type"]) is "object")
            {
                jsonObject["additionalProperties"] = false;

                if (jsonObject["properties"] is JsonObject properties)
                {
                    JsonArray required = [];
                    foreach (KeyValuePair<string, JsonNode?> property in properties)
                    {
                        required.Add((JsonNode?)JsonValue.Create(property.Key));
                    }

                    jsonObject["required"] = required;
                }
            }

            foreach (KeyValuePair<string, JsonNode?> property in jsonObject.ToArray())
            {
                if (property.Value is not null)
                {
                    NormalizeSchemaNode(property.Value);
                }
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (JsonNode? item in jsonArray)
            {
                if (item is not null)
                {
                    NormalizeSchemaNode(item);
                }
            }
        }
    }

    private static string? TryGetString(JsonNode? node)
    {
        try
        {
            return node?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static OpenAiChatCompletionRequestMessage MapChatCompletionMessage(ConversationRequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        JsonElement? reasoningDetails = CreateReasoningDetailsElement(message.ReasoningDetailsJson);
        string? reasoningContent = reasoningDetails is null
            ? message.ReasoningContent
            : null;

        if (message.ToolCalls.Count > 0)
        {
            OpenAiChatCompletionToolCall[] toolCalls = message.ToolCalls
                .Select(static toolCall => new OpenAiChatCompletionToolCall(
                    toolCall.Id,
                    "function",
                    new OpenAiChatCompletionFunctionCall(
                        toolCall.Name,
                        toolCall.ArgumentsJson)))
                .ToArray();

            return new OpenAiChatCompletionRequestMessage(
                message.Role,
                CreateChatCompletionContentElement(message),
                null,
                toolCalls,
                reasoningContent,
                reasoningDetails);
        }

        return new OpenAiChatCompletionRequestMessage(
            message.Role,
            CreateChatCompletionContentElement(message),
            message.ToolCallId,
            ReasoningContent: reasoningContent,
            ReasoningDetails: reasoningDetails);
    }

    private static JsonElement? CreateReasoningDetailsElement(string? reasoningDetailsJson)
    {
        if (string.IsNullOrWhiteSpace(reasoningDetailsJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(reasoningDetailsJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? CreateChatCompletionContentElement(ConversationRequestMessage message)
    {
        if (message.Attachments.Count == 0)
        {
            return string.IsNullOrWhiteSpace(message.Content)
                ? null
                : CreateStringContentElement(message.Content);
        }

        List<OpenAiChatCompletionContentPart> parts = [];
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            parts.Add(new OpenAiChatCompletionContentPart("text", message.Content));
        }

        foreach (ConversationAttachment attachment in message.Attachments)
        {
            if (attachment.IsImage)
            {
                parts.Add(new OpenAiChatCompletionContentPart(
                    "image_url",
                    ImageUrl: new OpenAiChatCompletionImageUrl(attachment.ToDataUri())));
                continue;
            }

            parts.Add(new OpenAiChatCompletionContentPart(
                "text",
                FormatAttachmentText(attachment)));
        }

        return JsonSerializer.SerializeToElement(
            parts,
            OpenAiConversationJsonContext.Default.IReadOnlyListOpenAiChatCompletionContentPart);
    }

    private static JsonElement CreateStringContentElement(string? value)
    {
        return JsonSerializer.SerializeToElement(
            value ?? string.Empty,
            OpenAiConversationJsonContext.Default.String);
    }

    private static IReadOnlyList<OpenAiResponsesContentPart> CreateResponsesContentParts(
        string contentType,
        ConversationRequestMessage message)
    {
        List<OpenAiResponsesContentPart> parts = [];

        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            parts.Add(new OpenAiResponsesContentPart(contentType, Text: message.Content));
        }

        foreach (ConversationAttachment attachment in message.Attachments)
        {
            if (attachment.IsImage)
            {
                parts.Add(new OpenAiResponsesContentPart(
                    "input_image",
                    ImageUrl: attachment.ToDataUri()));
                continue;
            }

            parts.Add(new OpenAiResponsesContentPart(
                contentType,
                Text: FormatAttachmentText(attachment)));
        }

        return parts;
    }

    private static string FormatAttachmentText(ConversationAttachment attachment)
    {
        string content = attachment.IsText
            ? attachment.TextContent!
            : attachment.ContentBase64;
        string encoding = attachment.IsText
            ? "text"
            : "base64";

        return $"""
            Attached file: {attachment.Name}
            Content-Type: {attachment.MediaType}
            Encoding: {encoding}

            {content}
            """;
    }
}
