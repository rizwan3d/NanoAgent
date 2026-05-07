namespace NanoAgent.Domain.Models;

public static class ProviderKindExtensions
{
    private const string OpenAiBaseUrl = "https://api.openai.com/v1";
    private const string GoogleAiStudioBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai";
    private const string AnthropicBaseUrl = "https://api.anthropic.com/v1";
    private const string OpenAiChatGptAccountBaseUrl = "https://chatgpt.com/backend-api/" + "co" + "dex";
    private const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1";
    private const string GitHubCopilotBaseUrl = "https://api.individual.githubcopilot.com";
    private const string KiloCodeBaseUrl = "https://api.kilo.ai/api/gateway";
    private const string OllamaBaseUrl = "http://127.0.0.1:11434/v1";
    private const string OllamaCloudBaseUrl = "https://ollama.com";
    private const string CerebrasBaseUrl = "https://api.cerebras.ai/v1";
    private const string GroqBaseUrl = "https://api.groq.com/openai/v1";
    private const string GoogleCodeAssistBaseUrl = "https://cloudcode-pa.googleapis.com";
    private const string OllamaApiKeyPlaceholder = "ollama";

    public static string ToDisplayName(this ProviderKind providerKind)
    {
        return providerKind switch
        {
            ProviderKind.OpenAi => "OpenAI",
            ProviderKind.OpenAiChatGptAccount => "OpenAI ChatGPT Plus/Pro",
            ProviderKind.GoogleAiStudio => "Google AI Studio",
            ProviderKind.Anthropic => "Anthropic",
            ProviderKind.AnthropicClaudeAccount => "Anthropic Claude Pro/Max",
            ProviderKind.GitHubCopilot => "GitHub Copilot",
            ProviderKind.OpenRouter => "OpenRouter",
            ProviderKind.KiloCode => "Kilo Code",
            ProviderKind.GoogleAntigravity => "Google Antigravity",
            ProviderKind.Ollama => "Ollama",
            ProviderKind.OllamaCloud => "Ollama Cloud",
            ProviderKind.Cerebras => "Cerebras",
            ProviderKind.Groq => "Groq",
            ProviderKind.GeminiCli => "Gemini CLI",
            ProviderKind.OpenAiCompatible => "OpenAI-compatible provider",
            _ => providerKind.ToString()
        };
    }

    public static string? GetManagedBaseUrl(this ProviderKind providerKind)
    {
        return providerKind switch
        {
            ProviderKind.OpenAi => OpenAiBaseUrl,
            ProviderKind.OpenAiChatGptAccount => OpenAiChatGptAccountBaseUrl,
            ProviderKind.GoogleAiStudio => GoogleAiStudioBaseUrl,
            ProviderKind.Anthropic => AnthropicBaseUrl,
            ProviderKind.AnthropicClaudeAccount => AnthropicBaseUrl,
            ProviderKind.GitHubCopilot => GitHubCopilotBaseUrl,
            ProviderKind.OpenRouter => OpenRouterBaseUrl,
            ProviderKind.KiloCode => KiloCodeBaseUrl,
            ProviderKind.GoogleAntigravity => GoogleCodeAssistBaseUrl,
            ProviderKind.Ollama => OllamaBaseUrl,
            ProviderKind.OllamaCloud => OllamaCloudBaseUrl,
            ProviderKind.Cerebras => CerebrasBaseUrl,
            ProviderKind.Groq => GroqBaseUrl,
            ProviderKind.GeminiCli => GoogleCodeAssistBaseUrl,
            _ => null
        };
    }

    public static string? GetDefaultApiKey(this ProviderKind providerKind)
    {
        return providerKind switch
        {
            ProviderKind.Ollama => OllamaApiKeyPlaceholder,
            _ => null
        };
    }
}
