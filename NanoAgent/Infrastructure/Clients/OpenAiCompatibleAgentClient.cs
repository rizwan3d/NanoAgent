using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NanoAgent;

internal sealed class OpenAiCompatibleAgentClient : IAgentClient
{
    private readonly string _endpoint;
    private readonly string _model;
    private readonly AgentPromptFactory _promptFactory;
    private readonly FileToolService _fileToolService;
    private readonly AppRuntimeOptions _runtimeOptions;
    private readonly IChatConsole _chatConsole;
    private readonly HttpClient _httpClient;

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
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> GetResponseAsync(string userPrompt)
    {
        try
        {
            List<ChatMessage> messages =
            [
                new ChatMessage { Role = ChatRole.System, Content = _promptFactory.CreateSystemPrompt() },
                new ChatMessage { Role = ChatRole.User, Content = userPrompt }
            ];

            for (int iteration = 0; iteration < 8; iteration++)
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
                    return !string.IsNullOrWhiteSpace(message.Content)
                        ? message.Content.Trim()
                        : BuildEmptyResponseMessage(message);
                }

                messages.Add(new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = message.Content ?? string.Empty,
                    ToolCalls = message.ToolCalls
                });

                foreach (ChatToolCall toolCall in message.ToolCalls)
                {
                    WriteVerbose($"tool call: {toolCall.Function.Name} {toolCall.Function.Arguments}");
                    string toolResult = _fileToolService.Execute(toolCall);
                    WriteVerbose($"tool result: {SummarizeToolResult(toolResult)}");
                    messages.Add(new ChatMessage
                    {
                        Role = ChatRole.Tool,
                        ToolCallId = toolCall.Id,
                        Content = toolResult
                    });
                }
            }

            return "Error: tool call loop exceeded the maximum number of iterations.";
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
            MaxTokens = 2048,
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

    private static string SummarizeToolResult(string toolResult)
    {
        string firstLine = toolResult.Replace("\r\n", "\n").Split('\n', 2)[0];
        return firstLine.Length <= 120 ? firstLine : firstLine[..117] + "...";
    }

    private static string BuildEmptyResponseMessage(ChatMessage message)
    {
        string role = string.IsNullOrWhiteSpace(message.Role) ? "<unknown>" : message.Role;
        return $"Assistant returned no final text response. role={role}";
    }
}
