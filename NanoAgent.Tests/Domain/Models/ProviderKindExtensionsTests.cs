using FluentAssertions;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Domain.Models;

public sealed class ProviderKindExtensionsTests
{
    [Theory]
    [InlineData(ProviderKind.OpenAi, "OpenAI")]
    [InlineData(ProviderKind.OpenAiChatGptAccount, "OpenAI ChatGPT Plus/Pro")]
    [InlineData(ProviderKind.GoogleAiStudio, "Google AI Studio")]
    [InlineData(ProviderKind.Anthropic, "Anthropic")]
    [InlineData(ProviderKind.AnthropicClaudeAccount, "Anthropic Claude Pro/Max")]
    [InlineData(ProviderKind.GitHubCopilot, "GitHub Copilot")]
    [InlineData(ProviderKind.OpenRouter, "OpenRouter")]
    [InlineData(ProviderKind.KiloCode, "Kilo Code")]
    [InlineData(ProviderKind.Ollama, "Ollama")]
    [InlineData(ProviderKind.LmStudio, "LM Studio")]
    [InlineData(ProviderKind.OllamaCloud, "Ollama Cloud")]
    [InlineData(ProviderKind.Cerebras, "Cerebras")]
    [InlineData(ProviderKind.Groq, "Groq")]
    [InlineData(ProviderKind.OpenCodeZen, "OpenCode Zen")]
    [InlineData(ProviderKind.OpenAiCompatible, "OpenAI-compatible provider")]
    public void ToDisplayName_Should_ReturnExpectedName_For_EachProviderKind(ProviderKind kind, string expected)
    {
        string result = kind.ToDisplayName();

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ProviderKind.OpenAi, "https://api.openai.com/v1")]
    [InlineData(ProviderKind.OpenAiChatGptAccount, "https://chatgpt.com/backend-api/co" + "dex")]
    [InlineData(ProviderKind.GoogleAiStudio, "https://generativelanguage.googleapis.com/v1beta/openai")]
    [InlineData(ProviderKind.Anthropic, "https://api.anthropic.com/v1")]
    [InlineData(ProviderKind.AnthropicClaudeAccount, "https://api.anthropic.com/v1")]
    [InlineData(ProviderKind.GitHubCopilot, "https://api.individual.githubcopilot.com")]
    [InlineData(ProviderKind.OpenRouter, "https://openrouter.ai/api/v1")]
    [InlineData(ProviderKind.KiloCode, "https://api.kilo.ai/api/gateway")]
    [InlineData(ProviderKind.Ollama, "http://127.0.0.1:11434/v1")]
    [InlineData(ProviderKind.LmStudio, "http://127.0.0.1:1234/v1")]
    [InlineData(ProviderKind.OllamaCloud, "https://ollama.com")]
    [InlineData(ProviderKind.Cerebras, "https://api.cerebras.ai/v1")]
    [InlineData(ProviderKind.Groq, "https://api.groq.com/openai/v1")]
    [InlineData(ProviderKind.OpenCodeZen, "https://opencode.ai/zen/v1")]
    public void GetManagedBaseUrl_Should_ReturnBaseUrl_For_ManagedProviders(ProviderKind kind, string expected)
    {
        string? result = kind.GetManagedBaseUrl();

        result.Should().Be(expected);
    }

    [Fact]
    public void GetManagedBaseUrl_Should_ReturnNull_For_OpenAiCompatible()
    {
        string? result = ProviderKind.OpenAiCompatible.GetManagedBaseUrl();

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(ProviderKind.Ollama, "ollama")]
    [InlineData(ProviderKind.OpenAi, null)]
    [InlineData(ProviderKind.Anthropic, null)]
    [InlineData(ProviderKind.GitHubCopilot, null)]
    [InlineData(ProviderKind.LmStudio, null)]
    [InlineData(ProviderKind.OpenAiCompatible, null)]
    public void GetDefaultApiKey_Should_ReturnExpected(ProviderKind kind, string? expected)
    {
        string? result = kind.GetDefaultApiKey();

        result.Should().Be(expected);
    }

    [Fact]
    public void ToDisplayName_Should_ReturnEnumToString_For_UnknownValue()
    {
        ProviderKind unknown = (ProviderKind)999;

        string result = unknown.ToDisplayName();

        result.Should().Be("999");
    }

    [Fact]
    public void GetManagedBaseUrl_Should_ReturnNull_For_UnknownValue()
    {
        ProviderKind unknown = (ProviderKind)999;

        string? result = unknown.GetManagedBaseUrl();

        result.Should().BeNull();
    }
}
