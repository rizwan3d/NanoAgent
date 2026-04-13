using System.Text;
using System.Text.Json;

namespace NanoAgent;

internal sealed class OpenAiCompatibleCompletionReader
{
    public async Task<ChatCompletionResponse?> ReadCompletionAsync(HttpResponseMessage response, OpenAiCompatibleProgressTracker tracker)
    {
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        return mediaType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true
            ? await ReadStreamingCompletionAsync(response, tracker)
            : await ReadBufferedCompletionAsync(response, tracker);
    }

    private static async Task<ChatCompletionResponse?> ReadBufferedCompletionAsync(
        HttpResponseMessage response,
        OpenAiCompatibleProgressTracker tracker)
    {
        string responseBody = await response.Content.ReadAsStringAsync();
        ChatCompletionResponse? completion = JsonSerializer.Deserialize(
            responseBody,
            NanoAgentJsonContext.Default.ChatCompletionResponse);

        if (completion?.Usage?.CompletionTokens > 0)
        {
            tracker.CompleteRequestWithExactTokens(completion.Usage.CompletionTokens);
        }
        else if (completion?.Choices?.FirstOrDefault()?.Message is ChatMessage message)
        {
            int estimate = EstimateOutputTokens(
                new StringBuilder(message.Content ?? string.Empty),
                message.ToolCalls);
            tracker.UpdateCurrentEstimate(estimate);
        }

        return completion;
    }

    private static async Task<ChatCompletionResponse?> ReadStreamingCompletionAsync(
        HttpResponseMessage response,
        OpenAiCompatibleProgressTracker tracker)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using StreamReader reader = new(stream);
        StringBuilder eventData = new();
        StringBuilder contentBuilder = new();
        Dictionary<int, StreamingToolCallState> toolCalls = [];
        string role = ChatRole.Assistant;
        ChatUsage? usage = null;

        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (eventData.Length > 0)
                {
                    ProcessSseEvent(
                        eventData.ToString(),
                        contentBuilder,
                        toolCalls,
                        tracker,
                        ref role,
                        ref usage);
                    eventData.Clear();
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                eventData.AppendLine(line["data:".Length..].TrimStart());
            }
        }

        if (eventData.Length > 0)
        {
            ProcessSseEvent(
                eventData.ToString(),
                contentBuilder,
                toolCalls,
                tracker,
                ref role,
                ref usage);
        }

        ChatToolCall[]? finalizedToolCalls = toolCalls.Count == 0
            ? null
            : toolCalls.OrderBy(pair => pair.Key).Select(pair => pair.Value.Build()).ToArray();

        return new ChatCompletionResponse
        {
            Choices =
            [
                new ChatChoice
                {
                    Message = new ChatMessage
                    {
                        Role = role,
                        Content = contentBuilder.ToString(),
                        ToolCalls = finalizedToolCalls
                    }
                }
            ],
            Usage = usage
        };
    }

    private static void ProcessSseEvent(
        string eventPayload,
        StringBuilder contentBuilder,
        Dictionary<int, StreamingToolCallState> toolCalls,
        OpenAiCompatibleProgressTracker tracker,
        ref string role,
        ref ChatUsage? usage)
    {
        foreach (string rawEvent in eventPayload
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(rawEvent, "[DONE]", StringComparison.Ordinal))
            {
                continue;
            }

            ChatCompletionResponse? chunk = JsonSerializer.Deserialize(
                rawEvent,
                NanoAgentJsonContext.Default.ChatCompletionResponse);

            if (chunk is null)
            {
                continue;
            }

            if (chunk.Usage is not null && chunk.Usage.CompletionTokens > 0)
            {
                usage = chunk.Usage;
                tracker.CompleteRequestWithExactTokens(chunk.Usage.CompletionTokens);
            }

            foreach (ChatChoice choice in chunk.Choices)
            {
                ChatMessageDelta? delta = choice.Delta;
                if (delta is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(delta.Role))
                {
                    role = delta.Role;
                }

                if (!string.IsNullOrEmpty(delta.Content))
                {
                    contentBuilder.Append(delta.Content);
                }

                if (delta.ToolCalls is not null)
                {
                    foreach (ChatToolCallDelta toolCallDelta in delta.ToolCalls)
                    {
                        if (!toolCalls.TryGetValue(toolCallDelta.Index, out StreamingToolCallState? state))
                        {
                            state = new StreamingToolCallState();
                            toolCalls[toolCallDelta.Index] = state;
                        }

                        state.Apply(toolCallDelta);
                    }
                }
            }

            if (!tracker.CurrentRequestIsExact)
            {
                tracker.UpdateCurrentEstimate(EstimateOutputTokens(contentBuilder, toolCalls));
            }
        }
    }

    private static int EstimateOutputTokens(StringBuilder contentBuilder, Dictionary<int, StreamingToolCallState> toolCalls)
    {
        int characterCount = contentBuilder.Length;

        foreach (StreamingToolCallState toolCall in toolCalls.Values)
        {
            characterCount += toolCall.Name.Length;
            characterCount += toolCall.Arguments.Length;
        }

        return Math.Max(1, (int)Math.Ceiling(characterCount / 4d));
    }

    private static int EstimateOutputTokens(StringBuilder contentBuilder, ChatToolCall[]? toolCalls)
    {
        int characterCount = contentBuilder.Length;

        if (toolCalls is not null)
        {
            foreach (ChatToolCall toolCall in toolCalls)
            {
                characterCount += toolCall.Function.Name.Length;
                characterCount += toolCall.Function.Arguments.Length;
            }
        }

        return Math.Max(1, (int)Math.Ceiling(characterCount / 4d));
    }

    private sealed class StreamingToolCallState
    {
        private readonly StringBuilder _name = new();
        private readonly StringBuilder _arguments = new();

        public string Id { get; private set; } = string.Empty;

        public string Type { get; private set; } = "function";

        public string Name => _name.ToString();

        public string Arguments => _arguments.ToString();

        public void Apply(ChatToolCallDelta delta)
        {
            if (!string.IsNullOrWhiteSpace(delta.Id))
            {
                Id = delta.Id;
            }

            if (!string.IsNullOrWhiteSpace(delta.Type))
            {
                Type = delta.Type!;
            }

            if (!string.IsNullOrWhiteSpace(delta.Function?.Name))
            {
                _name.Append(delta.Function.Name);
            }

            if (!string.IsNullOrEmpty(delta.Function?.Arguments))
            {
                _arguments.Append(delta.Function.Arguments);
            }
        }

        public ChatToolCall Build() =>
            new()
            {
                Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
                Type = string.IsNullOrWhiteSpace(Type) ? "function" : Type,
                Function = new ChatToolFunctionCall
                {
                    Name = Name,
                    Arguments = _arguments.Length == 0 ? "{}" : _arguments.ToString()
                }
            };
    }
}
