namespace NanoAgent.Domain.Models;

public static class ProviderKindExtensions
{
    private const string OpenAiBaseUrl = "https://api.openai.com/v1";
    private const string GoogleAiStudioBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai";
    private const string AnthropicBaseUrl = "https://api.anthropic.com/v1";
    private const string OpenAiChatGptAccountBaseUrl = "https://chatgpt.com/backend-api/" + "co" + "dex";
    private const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1";

    public static string ToDisplayName(this ProviderKind providerKind)
    {
        return providerKind switch
        {
            ProviderKind.OpenAi => "OpenAI",
            ProviderKind.OpenAiChatGptAccount => "OpenAI ChatGPT Plus/Pro",
            ProviderKind.GoogleAiStudio => "Google AI Studio",
            ProviderKind.Anthropic => "Anthropic",
            ProviderKind.AnthropicClaudeAccount => "Anthropic Claude Pro/Max",
            ProviderKind.OpenRouter => "OpenRouter",
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
            ProviderKind.OpenRouter => OpenRouterBaseUrl,
            _ => null
        };
    }
}
