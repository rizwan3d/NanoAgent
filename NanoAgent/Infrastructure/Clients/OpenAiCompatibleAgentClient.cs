using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NanoAgent;

internal sealed class OpenAiCompatibleAgentClient : IAgentClient
{
    private readonly string _endpoint;
    private readonly string _model;
    private readonly AgentPromptFactory _promptFactory;
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleAgentClient(string endpoint, string model, AgentPromptFactory promptFactory)
    {
        _endpoint = endpoint.TrimEnd('/');
        _model = model;
        _promptFactory = promptFactory;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> GetResponseAsync(string userPrompt)
    {
        try
        {
            ChatCompletionRequest request = CreateRequest(userPrompt);
            string payload = JsonSerializer.Serialize(request, NanoAgentJsonContext.Default.ChatCompletionRequest);
            using StringContent content = new(payload, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.PostAsync($"{_endpoint}/chat/completions", content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"Error: API returned status code {(int)response.StatusCode} ({response.StatusCode}). {responseBody}";
            }

            ChatCompletionResponse? completion = JsonSerializer.Deserialize(
                responseBody,
                NanoAgentJsonContext.Default.ChatCompletionResponse);

            return completion?.Choices?
                .FirstOrDefault()?
                .Message?
                .Content?
                .Trim() switch
            {
                { Length: > 0 } contentText => contentText,
                _ => "Unable to parse response"
            };
        }
        catch (Exception ex)
        {
            return $"Error communicating with LLM: {ex.Message}";
        }
    }

    private ChatCompletionRequest CreateRequest(string userPrompt) =>
        new()
        {
            Model = _model,
            Temperature = 0.7,
            MaxTokens = 2048,
            Messages =
            [
                new ChatMessage { Role = ChatRole.System, Content = _promptFactory.CreateSystemPrompt() },
                new ChatMessage { Role = ChatRole.User, Content = userPrompt }
            ]
        };
}
