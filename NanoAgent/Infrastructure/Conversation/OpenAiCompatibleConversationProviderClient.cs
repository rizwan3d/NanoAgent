using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Anthropic;
using NanoAgent.Infrastructure.OpenAi;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class OpenAiCompatibleConversationProviderClient : IConversationProviderClient
{
    private const int MaxRetryAttempts = 3;
    private const int AnthropicClaudeAccountMaxTokens = 8192;
    private const string AnthropicClaudeAccountBetaHeader = "claude-code-20250219,oauth-2025-04-20";
    private const string AnthropicClaudeAccountUserAgent = "claude-cli/2.1.75";
    private const string AnthropicVersion = "2023-06-01";
    private const string OpenRouterApplicationTitle = "NanoAgent";
    private const string OpenRouterApplicationUrl = "https://github.com/rizwan3d/NanoAgent";
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly HttpClient _httpClient;
    private readonly Func<double> _nextJitter;
    private readonly IAnthropicClaudeAccountCredentialService? _anthropicClaudeAccountCredentialService;
    private readonly IOpenAiChatGptAccountCredentialService? _openAiChatGptAccountCredentialService;
    private readonly ILogger<OpenAiCompatibleConversationProviderClient> _logger;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");

    public OpenAiCompatibleConversationProviderClient(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleConversationProviderClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<double>? nextJitter = null,
        IOpenAiChatGptAccountCredentialService? openAiChatGptAccountCredentialService = null,
        IAnthropicClaudeAccountCredentialService? anthropicClaudeAccountCredentialService = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
        _nextJitter = nextJitter ?? Random.Shared.NextDouble;
        _openAiChatGptAccountCredentialService = openAiChatGptAccountCredentialService;
        _anthropicClaudeAccountCredentialService = anthropicClaudeAccountCredentialService;
    }

    public async Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ProviderProfile.ProviderKind == ProviderKind.OpenAiChatGptAccount)
        {
            return await SendOpenAiChatGptAccountAsync(request, cancellationToken);
        }

        if (request.ProviderProfile.ProviderKind == ProviderKind.AnthropicClaudeAccount)
        {
            return await SendAnthropicClaudeAccountAsync(request, cancellationToken);
        }

        OpenAiChatCompletionRequest payload = BuildRequestPayload(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.OpenAiChatCompletionRequest);

        Uri baseUri = request.ProviderProfile.ResolveBaseUri();
        int retryCount = 0;

        for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
        {
            using HttpRequestMessage httpRequest = CreateHttpRequest(
                baseUri,
                request.ProviderProfile.ProviderKind,
                request.ApiKey,
                requestBody);
            LogDebugApiRequest(httpRequest.Method, httpRequest.RequestUri, requestBody);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            LogDebugApiResponse(response.StatusCode, TryGetResponseId(response), responseBody);

            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    throw new ConversationProviderException(
                        "The provider returned an empty response body for the conversation request.");
                }

                return new ConversationProviderPayload(
                    request.ProviderProfile.ProviderKind,
                    responseBody,
                    TryGetResponseId(response),
                    retryCount);
            }

            if (IsRetryableStatusCode(response.StatusCode) && attempt < MaxRetryAttempts)
            {
                retryCount++;
                TimeSpan retryDelay = CalculateRetryDelay(retryCount, response.Headers.RetryAfter);
                _logger.LogWarning(
                    "Provider returned retryable HTTP {StatusCode} on attempt {Attempt} of {MaxAttempts}. Retrying after {RetryDelayMilliseconds} ms.",
                    (int)response.StatusCode,
                    attempt + 1,
                    MaxRetryAttempts + 1,
                    Math.Round(retryDelay.TotalMilliseconds, MidpointRounding.AwayFromZero));
                await _delayAsync(retryDelay, cancellationToken);
                continue;
            }

            ThrowConversationRequestFailed(response.StatusCode, responseBody);
        }

        throw new ConversationProviderException(
            "Unable to complete the conversation request. The provider retry loop ended unexpectedly.");
    }

    private async Task<ConversationProviderPayload> SendOpenAiChatGptAccountAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (_openAiChatGptAccountCredentialService is null)
        {
            throw new ConversationProviderException(
                "OpenAI ChatGPT Plus/Pro credentials cannot be resolved in this runtime.");
        }

        OpenAiResponsesRequest payload = BuildResponsesRequestPayload(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.OpenAiResponsesRequest);
        Uri baseUri = request.ProviderProfile.ResolveBaseUri();
        OpenAiChatGptAccountResolvedCredential credential =
            await _openAiChatGptAccountCredentialService.ResolveAsync(
                request.ApiKey,
                forceRefresh: false,
                cancellationToken);
        int retryCount = 0;
        bool forcedRefreshAfterAuthFailure = false;

        for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
        {
            using HttpRequestMessage httpRequest = CreateOpenAiChatGptAccountHttpRequest(
                baseUri,
                credential,
                requestBody);
            LogDebugApiRequest(httpRequest.Method, httpRequest.RequestUri, requestBody);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            LogDebugApiResponse(response.StatusCode, TryGetResponseId(response), responseBody);

            if (response.IsSuccessStatusCode)
            {
                string normalizedResponseBody = NormalizeOpenAiChatGptAccountResponseBody(responseBody);
                if (string.IsNullOrWhiteSpace(normalizedResponseBody))
                {
                    throw new ConversationProviderException(
                        "The provider returned an empty response body for the conversation request.");
                }

                return new ConversationProviderPayload(
                    request.ProviderProfile.ProviderKind,
                    normalizedResponseBody,
                    TryGetResponseId(response),
                    retryCount);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !forcedRefreshAfterAuthFailure)
            {
                forcedRefreshAfterAuthFailure = true;
                credential = await _openAiChatGptAccountCredentialService.ResolveAsync(
                    request.ApiKey,
                    forceRefresh: true,
                    cancellationToken);
                continue;
            }

            if (IsRetryableStatusCode(response.StatusCode) && attempt < MaxRetryAttempts)
            {
                retryCount++;
                TimeSpan retryDelay = CalculateRetryDelay(retryCount, response.Headers.RetryAfter);
                _logger.LogWarning(
                    "Provider returned retryable HTTP {StatusCode} on attempt {Attempt} of {MaxAttempts}. Retrying after {RetryDelayMilliseconds} ms.",
                    (int)response.StatusCode,
                    attempt + 1,
                    MaxRetryAttempts + 1,
                    Math.Round(retryDelay.TotalMilliseconds, MidpointRounding.AwayFromZero));
                await _delayAsync(retryDelay, cancellationToken);
                continue;
            }

            ThrowConversationRequestFailed(response.StatusCode, responseBody);
        }

        throw new ConversationProviderException(
            "Unable to complete the conversation request. The provider retry loop ended unexpectedly.");
    }

    private async Task<ConversationProviderPayload> SendAnthropicClaudeAccountAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (_anthropicClaudeAccountCredentialService is null)
        {
            throw new ConversationProviderException(
                "Anthropic Claude Pro/Max credentials cannot be resolved in this runtime.");
        }

        AnthropicMessagesRequest payload = BuildAnthropicMessagesRequest(request);
        string requestBody = JsonSerializer.Serialize(
            payload,
            OpenAiConversationJsonContext.Default.AnthropicMessagesRequest);
        Uri baseUri = request.ProviderProfile.ResolveBaseUri();
        AnthropicClaudeAccountResolvedCredential credential =
            await _anthropicClaudeAccountCredentialService.ResolveAsync(
                request.ApiKey,
                forceRefresh: false,
                cancellationToken);
        int retryCount = 0;
        bool forcedRefreshAfterAuthFailure = false;

        for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++)
        {
            using HttpRequestMessage httpRequest = CreateAnthropicClaudeAccountHttpRequest(
                baseUri,
                credential,
                requestBody);
            LogDebugApiRequest(httpRequest.Method, httpRequest.RequestUri, requestBody);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            LogDebugApiResponse(response.StatusCode, TryGetResponseId(response), responseBody);

            if (response.IsSuccessStatusCode)
            {
                string normalizedResponseBody = ConvertAnthropicMessagesResponseToChatCompletion(
                    responseBody);
                if (string.IsNullOrWhiteSpace(normalizedResponseBody))
                {
                    throw new ConversationProviderException(
                        "The provider returned an empty response body for the conversation request.");
                }

                return new ConversationProviderPayload(
                    request.ProviderProfile.ProviderKind,
                    normalizedResponseBody,
                    TryGetResponseId(response),
                    retryCount);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !forcedRefreshAfterAuthFailure)
            {
                forcedRefreshAfterAuthFailure = true;
                credential = await _anthropicClaudeAccountCredentialService.ResolveAsync(
                    request.ApiKey,
                    forceRefresh: true,
                    cancellationToken);
                continue;
            }

            if (IsRetryableStatusCode(response.StatusCode) && attempt < MaxRetryAttempts)
            {
                retryCount++;
                TimeSpan retryDelay = CalculateRetryDelay(retryCount, response.Headers.RetryAfter);
                _logger.LogWarning(
                    "Provider returned retryable HTTP {StatusCode} on attempt {Attempt} of {MaxAttempts}. Retrying after {RetryDelayMilliseconds} ms.",
                    (int)response.StatusCode,
                    attempt + 1,
                    MaxRetryAttempts + 1,
                    Math.Round(retryDelay.TotalMilliseconds, MidpointRounding.AwayFromZero));
                await _delayAsync(retryDelay, cancellationToken);
                continue;
            }

            ThrowConversationRequestFailed(response.StatusCode, responseBody);
        }

        throw new ConversationProviderException(
            "Unable to complete the conversation request. The provider retry loop ended unexpectedly.");
    }

    private static HttpRequestMessage CreateHttpRequest(
        Uri baseUri,
        ProviderKind providerKind,
        string apiKey,
        string requestBody)
    {
        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, "chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (providerKind == ProviderKind.OpenRouter)
        {
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", OpenRouterApplicationUrl);
            httpRequest.Headers.TryAddWithoutValidation("X-Title", OpenRouterApplicationTitle);
        }

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private HttpRequestMessage CreateOpenAiChatGptAccountHttpRequest(
        Uri baseUri,
        OpenAiChatGptAccountResolvedCredential credential,
        string requestBody)
    {
        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, "responses"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        httpRequest.Headers.TryAddWithoutValidation("originator", "nanoagent");
        httpRequest.Headers.TryAddWithoutValidation("session_id", _sessionId);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", "NanoAgent/1.0");
        if (!string.IsNullOrWhiteSpace(credential.AccountId))
        {
            httpRequest.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credential.AccountId);
        }

        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private static HttpRequestMessage CreateAnthropicClaudeAccountHttpRequest(
        Uri baseUri,
        AnthropicClaudeAccountResolvedCredential credential,
        string requestBody)
    {
        HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(baseUri, "messages"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        httpRequest.Headers.Accept.ParseAdd("application/json");
        httpRequest.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        httpRequest.Headers.TryAddWithoutValidation("anthropic-beta", AnthropicClaudeAccountBetaHeader);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", AnthropicClaudeAccountUserAgent);
        httpRequest.Headers.TryAddWithoutValidation("x-app", "cli");
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private static OpenAiChatCompletionRequest BuildRequestPayload(ConversationProviderRequest request)
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
            messages.Add(MapMessage(message));
        }

        OpenAiChatCompletionToolDefinition[] tools = request.AvailableTools
            .Select(definition => new OpenAiChatCompletionToolDefinition(
                "function",
                new OpenAiChatCompletionFunctionDefinition(
                    definition.Name,
                    definition.Description,
                    definition.Schema)))
            .ToArray();

        // Intentionally omit max_tokens so the provider can use its maximum supported output/context policy.
        return new OpenAiChatCompletionRequest(
            request.ModelId,
            messages,
            tools,
            ReasoningEffortOptions.ToProviderValue(request.ReasoningEffort));
    }

    private static OpenAiResponsesRequest BuildResponsesRequestPayload(ConversationProviderRequest request)
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

    private static AnthropicMessagesRequest BuildAnthropicMessagesRequest(ConversationProviderRequest request)
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

        List<AnthropicContentBlock> system = [
            new AnthropicContentBlock(
                "text",
                Text: "You are Claude Code, Anthropic's official CLI for Claude.")
        ];
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

    private static string CreateToolCallId(string? rawId, string? responseId, int ordinal)
    {
        string? normalizedId = NormalizeOrNull(rawId);
        if (normalizedId is not null)
        {
            return normalizedId;
        }

        string? normalizedResponseId = NormalizeOrNull(responseId);
        return normalizedResponseId is null
            ? $"tool_call_{ordinal}"
            : $"{normalizedResponseId}_tool_call_{ordinal}";
    }

    private static string? NormalizeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string NormalizeOpenAiChatGptAccountResponseBody(string responseBody)
    {
        return OpenAiResponsesEventStreamParser.TryParseResponsePayload(responseBody) ?? responseBody;
    }

    private static string ConvertAnthropicMessagesResponseToChatCompletion(string responseBody)
    {
        AnthropicMessagesResponse? response = JsonSerializer.Deserialize(
            responseBody,
            OpenAiConversationJsonContext.Default.AnthropicMessagesResponse);
        if (response?.Content is null)
        {
            throw new ConversationProviderException(
                "The Anthropic Claude Pro/Max response did not contain message content.");
        }

        List<string> textParts = [];
        List<OpenAiChatCompletionToolCall> toolCalls = [];
        int toolCallOrdinal = 0;

        foreach (AnthropicResponseContentBlock contentBlock in response.Content)
        {
            if (string.Equals(contentBlock.Type, "text", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(contentBlock.Text))
            {
                textParts.Add(contentBlock.Text.Trim());
                continue;
            }

            if (string.Equals(contentBlock.Type, "tool_use", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(contentBlock.Name))
            {
                toolCalls.Add(new OpenAiChatCompletionToolCall(
                    CreateToolCallId(contentBlock.Id, response.Id, ++toolCallOrdinal),
                    "function",
                    new OpenAiChatCompletionFunctionCall(
                        contentBlock.Name.Trim(),
                        contentBlock.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                            ? "{}"
                            : contentBlock.Input.GetRawText())));
            }
        }

        string content = string.Join(
            Environment.NewLine + Environment.NewLine,
            textParts);
        OpenAiChatCompletionResponse convertedResponse = new(
            response.Id,
            [
                new OpenAiChatCompletionChoice(
                    new OpenAiChatCompletionResponseMessage(
                        CreateStringContentElement(content),
                        toolCalls.Count == 0 ? null : toolCalls,
                        FunctionCall: null,
                        Refusal: null),
                    MapAnthropicStopReason(response.StopReason))
            ],
            ConvertAnthropicUsage(response.Usage));

        return JsonSerializer.Serialize(
            convertedResponse,
            OpenAiConversationJsonContext.Default.OpenAiChatCompletionResponse);
    }

    private static string? MapAnthropicStopReason(string? stopReason)
    {
        return stopReason switch
        {
            "end_turn" => "stop",
            "tool_use" => "tool_calls",
            "max_tokens" => "length",
            "stop_sequence" => "stop",
            null => null,
            _ => stopReason
        };
    }

    private static OpenAiChatCompletionUsage? ConvertAnthropicUsage(AnthropicUsage? usage)
    {
        if (usage is null)
        {
            return null;
        }

        int? promptTokens = SumNullable(
            usage.InputTokens,
            usage.CacheReadInputTokens,
            usage.CacheCreationInputTokens);
        int? completionTokens = usage.OutputTokens;
        int? totalTokens = SumNullable(promptTokens, completionTokens);

        return new OpenAiChatCompletionUsage(
            completionTokens,
            promptTokens,
            totalTokens,
            usage.CacheReadInputTokens is null
                ? null
                : new OpenAiChatCompletionUsageDetails(usage.CacheReadInputTokens));
    }

    private static int? SumNullable(params int?[] values)
    {
        int sum = 0;
        bool hasValue = false;
        foreach (int? value in values)
        {
            if (value is null)
            {
                continue;
            }

            checked
            {
                sum += value.Value;
            }

            hasValue = true;
        }

        return hasValue ? sum : null;
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

        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            items.Add(new OpenAiResponsesInputItem(
                Role: message.Role,
                Content: CreateResponsesContentParts(contentType, message)));
        }
        else if (message.Attachments.Count > 0)
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

    private static OpenAiChatCompletionRequestMessage MapMessage(ConversationRequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

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
                toolCalls);
        }

        return new OpenAiChatCompletionRequestMessage(
            message.Role,
            CreateChatCompletionContentElement(message),
            message.ToolCallId);
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

    private static string? TryGetResponseId(HttpResponseMessage response)
    {
        return TryGetFirstHeaderValue(response, "x-request-id")
            ?? TryGetFirstHeaderValue(response, "request-id");
    }

    private static string? TryGetFirstHeaderValue(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        int numericStatusCode = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
            numericStatusCode is >= 500 and <= 599;
    }

    private TimeSpan CalculateRetryDelay(
        int retryCount,
        RetryConditionHeaderValue? retryAfter)
    {
        double exponentialMilliseconds = BaseRetryDelay.TotalMilliseconds *
            Math.Pow(2, Math.Max(0, retryCount - 1));
        TimeSpan exponentialDelay = TimeSpan.FromMilliseconds(
            Math.Min(exponentialMilliseconds, MaxRetryDelay.TotalMilliseconds));
        TimeSpan jitteredDelay = TimeSpan.FromMilliseconds(
            Math.Clamp(_nextJitter(), 0d, 1d) * exponentialDelay.TotalMilliseconds);
        TimeSpan? retryAfterDelay = GetRetryAfterDelay(retryAfter);

        return retryAfterDelay is { } serverDelay && serverDelay > jitteredDelay
            ? serverDelay
            : jitteredDelay;
    }

    private static TimeSpan? GetRetryAfterDelay(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            TimeSpan delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero
                ? delay
                : null;
        }

        return null;
    }

    private static void ThrowConversationRequestFailed(
        HttpStatusCode statusCode,
        string responseBody)
    {
        string detail = string.IsNullOrWhiteSpace(responseBody)
            ? $"Provider returned HTTP {(int)statusCode}."
            : $"Provider returned HTTP {(int)statusCode}: {Truncate(responseBody.Trim(), 200)}";

        throw new ConversationProviderException(
            $"Unable to complete the conversation request. {detail}");
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private void LogDebugApiRequest(
        HttpMethod method,
        Uri? requestUri,
        string requestBody)
    {
#if DEBUG
        _logger.LogInformation(
            "OpenAI-compatible chat API request {Method} {RequestUri}: {RequestBody}",
            method,
            requestUri,
            requestBody);
#endif
    }

    private void LogDebugApiResponse(
        System.Net.HttpStatusCode statusCode,
        string? responseId,
        string responseBody)
    {
#if DEBUG
        _logger.LogInformation(
            "OpenAI-compatible chat API response {StatusCode} {ResponseId}: {ResponseBody}",
            (int)statusCode,
            responseId ?? "(none)",
            responseBody);
#endif
    }
}
