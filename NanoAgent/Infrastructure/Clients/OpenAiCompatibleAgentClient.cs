using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NanoAgent;

internal sealed class OpenAiCompatibleAgentClient : IAgentClient
{
    private const int MaxToolIterations = 24;
    private readonly string _endpoint;
    private readonly IToolService _toolService;
    private readonly IChatSession _chatSession;
    private readonly AppRuntimeOptions _runtimeOptions;
    private readonly IChatConsole _chatConsole;
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleRequestFactory _requestFactory;
    private readonly OpenAiCompatibleCompletionReader _completionReader;
    private readonly OpenAiCompatibleToolCallPresenter _toolCallPresenter;

    public OpenAiCompatibleAgentClient(
        string endpoint,
        string model,
        string? apiKey,
        IToolService toolService,
        IChatSession chatSession,
        IChatConsole chatConsole,
        AppRuntimeOptions runtimeOptions)
    {
        _endpoint = endpoint.TrimEnd('/');
        _toolService = toolService;
        _chatSession = chatSession;
        _chatConsole = chatConsole;
        _runtimeOptions = runtimeOptions;
        _requestFactory = new OpenAiCompatibleRequestFactory(model, toolService);
        _completionReader = new OpenAiCompatibleCompletionReader();
        _toolCallPresenter = new OpenAiCompatibleToolCallPresenter(chatConsole, runtimeOptions);
        _httpClient = CreateHttpClient(apiKey);
    }

    public string SessionId => _chatSession.SessionId;

    public bool IsResumedSession => _chatSession.IsResumedSession;

    public async Task<string> GetResponseAsync(string userPrompt)
    {
        _chatConsole.BeginAgentActivity();
        Stopwatch stopwatch = Stopwatch.StartNew();
        OpenAiCompatibleProgressTracker tracker = new();
        using CancellationTokenSource progressCts = new();
        Task progressTask = RunProgressLoopAsync(stopwatch, tracker, progressCts.Token);

        try
        {
            return await ExecuteTurnLoopAsync(userPrompt, tracker);
        }
        catch (TaskCanceledException) when (!_runtimeOptions.Verbose)
        {
            return "Error communicating with LLM: the request was canceled before completion.";
        }
        catch (TaskCanceledException exception)
        {
            return $"Error communicating with LLM: the request was canceled before completion. {exception.Message}";
        }
        catch (Exception exception)
        {
            return $"Error communicating with LLM: {exception.Message}";
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

            _chatConsole.CompleteAgentActivity(stopwatch.Elapsed, tracker.DisplayTokens, tracker.IsEstimate);
        }
    }

    private async Task<string> ExecuteTurnLoopAsync(string userPrompt, OpenAiCompatibleProgressTracker tracker)
    {
        List<ChatMessage> messages = _chatSession.CreateTurnMessages(userPrompt);
        int autoContinueCount = 0;

        for (int iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            WriteVerbose($"chat iteration {iteration + 1}: sending request with {messages.Count} message(s)");
            ChatCompletionResponse? completion = await SendRequestAsync(_requestFactory.CreateRequest(messages), tracker);
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

                return FinalizeAssistantResponse(messages, message);
            }

            ExecuteToolCalls(messages, message.ToolCalls, message.Content);
        }

        return $"Error: tool call loop exceeded the maximum number of iterations ({MaxToolIterations}).";
    }

    private string FinalizeAssistantResponse(List<ChatMessage> messages, ChatMessage message)
    {
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

    private void ExecuteToolCalls(List<ChatMessage> messages, ChatToolCall[] toolCalls, string? assistantContent)
    {
        messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = assistantContent ?? string.Empty,
            ToolCalls = toolCalls
        });

        foreach (ChatToolCall toolCall in toolCalls)
        {
            _toolCallPresenter.RenderBeforeExecution(toolCall);
            string toolResult = _toolService.Execute(toolCall);
            _toolCallPresenter.RenderAfterExecution(toolCall, toolResult);
            messages.Add(new ChatMessage
            {
                Role = ChatRole.Tool,
                ToolCallId = toolCall.Id,
                Content = toolResult
            });
        }
    }

    private async Task<ChatCompletionResponse?> SendRequestAsync(ChatCompletionRequest request, OpenAiCompatibleProgressTracker tracker)
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

        return await _completionReader.ReadCompletionAsync(response, tracker);
    }

    private async Task RunProgressLoopAsync(Stopwatch stopwatch, OpenAiCompatibleProgressTracker tracker, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(200));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            _chatConsole.UpdateAgentActivity(stopwatch.Elapsed, tracker.DisplayTokens, tracker.IsEstimate);
        }
    }

    private void WriteVerbose(string message)
    {
        if (_runtimeOptions.Verbose)
        {
            _chatConsole.RenderVerboseMessage(message);
        }
    }

    private static HttpClient CreateHttpClient(string? apiKey)
    {
        string? trimmedApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        HttpClient client = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (trimmedApiKey is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", trimmedApiKey);
        }

        return client;
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
}
