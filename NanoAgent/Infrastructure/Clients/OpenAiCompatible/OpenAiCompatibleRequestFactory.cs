namespace NanoAgent;

internal sealed class OpenAiCompatibleRequestFactory
{
    private const int DefaultMaxTokens = 32000;
    private readonly string _model;
    private readonly IToolService _toolService;

    public OpenAiCompatibleRequestFactory(string model, IToolService toolService)
    {
        _model = model;
        _toolService = toolService;
    }

    public ChatCompletionRequest CreateRequest(List<ChatMessage> messages) =>
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
}
