using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NanoAgent;

internal sealed class OpenAiCompatibleAgentClient : IAgentClient
{
    private const int MaxToolIterations = 24;
    private const int DefaultMaxTokens = 32000;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly IToolService _toolService;
    private readonly IChatSession _chatSession;
    private readonly AppRuntimeOptions _runtimeOptions;
    private readonly IChatConsole _chatConsole;
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleAgentClient(
        string endpoint,
        string model,
        IToolService toolService,
        IChatSession chatSession,
        IChatConsole chatConsole,
        AppRuntimeOptions runtimeOptions)
    {
        _endpoint = endpoint.TrimEnd('/');
        _model = model;
        _toolService = toolService;
        _chatSession = chatSession;
        _chatConsole = chatConsole;
        _runtimeOptions = runtimeOptions;
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    public async Task<string> GetResponseAsync(string userPrompt)
    {
        _chatConsole.BeginAgentActivity();
        Stopwatch stopwatch = Stopwatch.StartNew();
        ProgressSnapshot snapshot = new();
        using CancellationTokenSource progressCts = new();
        Task progressTask = RunProgressLoopAsync(stopwatch, snapshot, progressCts.Token);

        try
        {
            List<ChatMessage> messages = _chatSession.CreateTurnMessages(userPrompt);
            int autoContinueCount = 0;

            for (int iteration = 0; iteration < MaxToolIterations; iteration++)
            {
                WriteVerbose($"chat iteration {iteration + 1}: sending request with {messages.Count} message(s)");
                ChatCompletionRequest request = CreateRequest(messages);
                snapshot.BeginRequest();
                ChatCompletionResponse? completion = await SendRequestAsync(request, snapshot);
                ChatMessage? message = completion?.Choices?.FirstOrDefault()?.Message;

                if (message is null)
                {
                    return "Unable to parse response";
                }

                if (message.ToolCalls is null || message.ToolCalls.Length == 0)
                {
                    if (ShouldAutoContinue(message.Content) && autoContinueCount < 4)
                    {
                        autoContinueCount++;
                        WriteVerbose("assistant returned an incomplete response; auto-continuing to finish the task");
                        AppendContinuePrompt(messages, message.Content);
                        continue;
                    }

                    string responseText = !string.IsNullOrWhiteSpace(message.Content)
                        ? message.Content.Trim()
                        : BuildEmptyResponseMessage(message);

                    messages.Add(new ChatMessage
                    {
                        Role = ChatRole.Assistant,
                        Content = message.Content ?? string.Empty
                    });
                    _chatSession.CommitTurn(messages);
                    return responseText;
                }

                messages.Add(new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = message.Content ?? string.Empty,
                    ToolCalls = message.ToolCalls
                });

                foreach (ChatToolCall toolCall in message.ToolCalls)
                {
                    MaybeRenderUserFacingCommand(toolCall);
                    WriteVerbose(FormatToolCallVerboseMessage(toolCall));
                    string toolResult = _toolService.Execute(toolCall);
                    WriteVerbose(FormatToolResultVerboseMessage(toolCall, toolResult));
                    messages.Add(new ChatMessage
                    {
                        Role = ChatRole.Tool,
                        ToolCallId = toolCall.Id,
                        Content = toolResult
                    });
                }
            }

            return $"Error: tool call loop exceeded the maximum number of iterations ({MaxToolIterations}).";
        }
        catch (TaskCanceledException) when (!_runtimeOptions.Verbose)
        {
            return "Error communicating with LLM: the request was canceled before completion.";
        }
        catch (TaskCanceledException ex)
        {
            return $"Error communicating with LLM: the request was canceled before completion. {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error communicating with LLM: {ex.Message}";
        }
        finally
        {
            stopwatch.Stop();
            progressCts.Cancel();

            try
            {
                await progressTask;
            }
            catch (OperationCanceledException)
            {
            }

            _chatConsole.CompleteAgentActivity(stopwatch.Elapsed, snapshot.DisplayTokens, snapshot.IsEstimate);
        }
    }

    private async Task<ChatCompletionResponse?> SendRequestAsync(ChatCompletionRequest request, ProgressSnapshot snapshot)
    {
        string payload = JsonSerializer.Serialize(request, NanoAgentJsonContext.Default.ChatCompletionRequest);
        using HttpRequestMessage requestMessage = new(HttpMethod.Post, $"{_endpoint}/chat/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"API returned status code {(int)response.StatusCode} ({response.StatusCode}). {errorBody}");
        }

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        return mediaType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true
            ? await ReadStreamingCompletionAsync(response, snapshot)
            : await ReadBufferedCompletionAsync(response, snapshot);
    }

    private async Task RunProgressLoopAsync(Stopwatch stopwatch, ProgressSnapshot snapshot, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(200));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            _chatConsole.UpdateAgentActivity(
                stopwatch.Elapsed,
                snapshot.DisplayTokens,
                snapshot.IsEstimate);
        }
    }

    private static async Task<ChatCompletionResponse?> ReadBufferedCompletionAsync(
        HttpResponseMessage response,
        ProgressSnapshot snapshot)
    {
        string responseBody = await response.Content.ReadAsStringAsync();
        ChatCompletionResponse? completion = JsonSerializer.Deserialize(
            responseBody,
            NanoAgentJsonContext.Default.ChatCompletionResponse);

        if (completion?.Usage?.CompletionTokens > 0)
        {
            snapshot.CompleteRequestWithExactTokens(completion.Usage.CompletionTokens);
        }
        else if (completion?.Choices?.FirstOrDefault()?.Message is ChatMessage message)
        {
            int estimate = EstimateOutputTokens(
                new StringBuilder(message.Content ?? string.Empty),
                message.ToolCalls);
            snapshot.UpdateCurrentEstimate(estimate);
        }

        return completion;
    }

    private static async Task<ChatCompletionResponse?> ReadStreamingCompletionAsync(
        HttpResponseMessage response,
        ProgressSnapshot snapshot)
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
                        snapshot,
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
                snapshot,
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
        ProgressSnapshot snapshot,
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
                snapshot.CompleteRequestWithExactTokens(chunk.Usage.CompletionTokens);
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

            if (!snapshot.CurrentRequestIsExact)
            {
                snapshot.UpdateCurrentEstimate(EstimateOutputTokens(contentBuilder, toolCalls));
            }
        }
    }

    private ChatCompletionRequest CreateRequest(List<ChatMessage> messages) =>
        new()
        {
            Model = _model,
            Temperature = 0.7,
            MaxTokens = DefaultMaxTokens,
            Messages = messages.ToArray(),
            Tools = _toolService.GetToolDefinitions(),
            Stream = true,
            StreamOptions = new ChatStreamOptions
            {
                IncludeUsage = true
            }
        };

    private void WriteVerbose(string message)
    {
        if (!_runtimeOptions.Verbose)
        {
            return;
        }

        _chatConsole.RenderVerboseMessage(message);
    }

    private void MaybeRenderUserFacingCommand(ChatToolCall toolCall)
    {
        if (_runtimeOptions.Verbose)
        {
            return;
        }

        if (!string.Equals(toolCall.Function.Name, "run_command", StringComparison.Ordinal))
        {
            return;
        }

        string? command = TryReadJsonStringProperty(toolCall.Function.Arguments, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        _chatConsole.RenderCommandMessage(command);
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

    private static string SummarizeToolResult(string toolResult)
    {
        string normalized = toolResult.Replace("\r\n", "\n");

        if (normalized.StartsWith("COMMAND:", StringComparison.Ordinal))
        {
            string[] lines = normalized.Split('\n');
            return string.Join(" | ", lines.Take(5));
        }

        string firstLine = normalized.Split('\n', 2)[0];
        return firstLine.Length <= 120 ? firstLine : firstLine[..117] + "...";
    }

    private static string FormatToolCallVerboseMessage(ChatToolCall toolCall)
    {
        if (!string.Equals(toolCall.Function.Name, "run_command", StringComparison.Ordinal))
        {
            return $"tool call: {toolCall.Function.Name} {toolCall.Function.Arguments}";
        }

        string command = TryReadJsonStringProperty(toolCall.Function.Arguments, "command") ?? toolCall.Function.Arguments;
        return $"tool call: run_command\ncommand: {command}\narguments: {toolCall.Function.Arguments}";
    }

    private static string FormatToolResultVerboseMessage(ChatToolCall toolCall, string toolResult)
    {
        string summary = SummarizeToolResult(toolResult);

        if (!string.Equals(toolCall.Function.Name, "run_command", StringComparison.Ordinal))
        {
            return $"tool result: {summary}";
        }

        return $"tool result:\n{summary}";
    }

    private static string? TryReadJsonStringProperty(string json, string propertyName)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty(propertyName, out JsonElement property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildEmptyResponseMessage(ChatMessage message)
    {
        string role = string.IsNullOrWhiteSpace(message.Role) ? "<unknown>" : message.Role;
        return $"Assistant returned no final text response. role={role}";
    }

    private static bool ShouldAutoContinue(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        string normalized = content.Trim().ToLowerInvariant();

        string[] continuationSignals =
        [
            "i will now",
            "i'll now",
            "i will ",
            "i'll ",
            "next, i",
            "to implement",
            "to proceed",
            "i need to",
            "must modify",
            "i should now",
            "the next step",
            "i'm going to"
        ];

        return continuationSignals.Any(normalized.Contains);
    }

    private static void AppendContinuePrompt(List<ChatMessage> messages, string? assistantContent)
    {
        messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = assistantContent ?? string.Empty
        });
        messages.Add(new ChatMessage
        {
            Role = ChatRole.User,
            Content = "Continue from your last step. Use the available tools if needed and finish the task instead of stopping at analysis or an empty response."
        });
    }

    private sealed class ProgressSnapshot
    {
        private readonly object _gate = new();
        private int _exactCompletedTokens;
        private int? _currentEstimatedTokens;
        private bool _currentRequestExact;

        public int? DisplayTokens
        {
            get
            {
                lock (_gate)
                {
                    int total = _exactCompletedTokens + (_currentEstimatedTokens ?? 0);
                    return total > 0 ? total : null;
                }
            }
        }

        public bool IsEstimate
        {
            get
            {
                lock (_gate)
                {
                    return _currentEstimatedTokens.HasValue && !_currentRequestExact;
                }
            }
        }

        public bool CurrentRequestIsExact
        {
            get
            {
                lock (_gate)
                {
                    return _currentRequestExact;
                }
            }
        }

        public void BeginRequest()
        {
            lock (_gate)
            {
                _currentEstimatedTokens = null;
                _currentRequestExact = false;
            }
        }

        public void UpdateCurrentEstimate(int estimatedTokens)
        {
            lock (_gate)
            {
                if (_currentRequestExact)
                {
                    return;
                }

                _currentEstimatedTokens = Math.Max(estimatedTokens, 1);
            }
        }

        public void CompleteRequestWithExactTokens(int exactTokens)
        {
            lock (_gate)
            {
                _exactCompletedTokens += Math.Max(exactTokens, 0);
                _currentEstimatedTokens = null;
                _currentRequestExact = true;
            }
        }
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
