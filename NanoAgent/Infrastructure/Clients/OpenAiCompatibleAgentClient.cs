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
    private readonly AgentPromptFactory _promptFactory;
    private readonly FileToolService _fileToolService;
    private readonly AppRuntimeOptions _runtimeOptions;
    private readonly IChatConsole _chatConsole;
    private readonly HttpClient _httpClient;
    private readonly List<ChatMessage> _sessionMessages;

    public OpenAiCompatibleAgentClient(
        string endpoint,
        string model,
        AgentPromptFactory promptFactory,
        FileToolService fileToolService,
        IChatConsole chatConsole,
        AppRuntimeOptions runtimeOptions)
    {
        _endpoint = endpoint.TrimEnd('/');
        _model = model;
        _promptFactory = promptFactory;
        _fileToolService = fileToolService;
        _chatConsole = chatConsole;
        _runtimeOptions = runtimeOptions;
        _httpClient = new HttpClient();
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _sessionMessages =
        [
            new ChatMessage
            {
                Role = ChatRole.System,
                Content = _promptFactory.CreateSystemPrompt()
            }
        ];
    }

    public async Task<string> GetResponseAsync(string userPrompt)
    {
        try
        {
            List<ChatMessage> messages = CloneMessages(_sessionMessages);
            messages.Add(new ChatMessage { Role = ChatRole.User, Content = userPrompt });
            int autoContinueCount = 0;

            for (int iteration = 0; iteration < MaxToolIterations; iteration++)
            {
                WriteVerbose($"chat iteration {iteration + 1}: sending request with {messages.Count} message(s)");
                ChatCompletionRequest request = CreateRequest(messages);
                ChatCompletionResponse? completion = await SendRequestAsync(request);
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
                    CommitSessionMessages(messages);
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
                    string toolResult = _fileToolService.Execute(toolCall);
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
    }

    private async Task<ChatCompletionResponse?> SendRequestAsync(ChatCompletionRequest request)
    {
        string payload = JsonSerializer.Serialize(request, NanoAgentJsonContext.Default.ChatCompletionRequest);
        using StringContent content = new(payload, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.PostAsync($"{_endpoint}/chat/completions", content);
        string responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"API returned status code {(int)response.StatusCode} ({response.StatusCode}). {responseBody}");
        }

        return JsonSerializer.Deserialize(responseBody, NanoAgentJsonContext.Default.ChatCompletionResponse);
    }

    private ChatCompletionRequest CreateRequest(List<ChatMessage> messages) =>
        new()
        {
            Model = _model,
            Temperature = 0.7,
            MaxTokens = DefaultMaxTokens,
            Messages = messages.ToArray(),
            Tools = _fileToolService.GetToolDefinitions()
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

    private void CommitSessionMessages(List<ChatMessage> messages)
    {
        _sessionMessages.Clear();
        _sessionMessages.AddRange(CloneMessages(messages));
    }

    private static List<ChatMessage> CloneMessages(IEnumerable<ChatMessage> messages) =>
        messages.Select(CloneMessage).ToList();

    private static ChatMessage CloneMessage(ChatMessage message) =>
        new()
        {
            Role = message.Role,
            Content = message.Content,
            ToolCallId = message.ToolCallId,
            ToolCalls = message.ToolCalls?.Select(CloneToolCall).ToArray()
        };

    private static ChatToolCall CloneToolCall(ChatToolCall toolCall) =>
        new()
        {
            Id = toolCall.Id,
            Type = toolCall.Type,
            Function = new ChatToolFunctionCall
            {
                Name = toolCall.Function.Name,
                Arguments = toolCall.Function.Arguments
            }
        };
}
