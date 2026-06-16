using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Conversation;

namespace NanoAgent.Tests.Infrastructure.Conversation;

public sealed class ConversationProviderRequestPayloadFactoryTests
{
    private readonly ConversationProviderRequestPayloadFactory _sut = new();

    [Fact]
    public void BuildChatCompletionRequest_Should_MapOpenAiReasoningEffort()
    {
        OpenAiChatCompletionRequest request = _sut.BuildChatCompletionRequest(CreateRequest(
            ProviderKind.OpenAi,
            "gpt-5.4",
            thinkingMode: "on",
            reasoningEffort: "high"));

        request.ReasoningEffort.Should().Be("high");
        request.Reasoning.Should().BeNull();
    }

    [Fact]
    public void BuildResponsesRequest_Should_MapOpenCodeZenReasoning()
    {
        OpenAiResponsesRequest request = _sut.BuildResponsesRequest(CreateRequest(
            ProviderKind.OpenCodeZen,
            "gpt-5.4",
            thinkingMode: "on",
            reasoningEffort: "high"));

        request.Reasoning.Should().NotBeNull();
        request.Reasoning!.Effort.Should().Be("high");
        request.Reasoning.Summary.Should().Be("auto");
    }

    [Fact]
    public void BuildAnthropicMessagesRequest_Should_UseAdaptiveThinking_ForClaude4Models()
    {
        AnthropicMessagesRequest request = _sut.BuildAnthropicMessagesRequest(CreateRequest(
            ProviderKind.Anthropic,
            "claude-sonnet-4-6",
            thinkingMode: "on",
            reasoningEffort: "xhigh"));

        request.Thinking.Should().NotBeNull();
        request.Thinking!.Type.Should().Be("adaptive");
        request.Thinking.Display.Should().Be("summarized");
        request.OutputConfig.Should().NotBeNull();
        request.OutputConfig!.Effort.Should().Be("xhigh");
    }

    [Fact]
    public void BuildAnthropicMessagesRequest_Should_FallBackToManualThinking_ForOlderClaudeModels()
    {
        AnthropicMessagesRequest request = _sut.BuildAnthropicMessagesRequest(CreateRequest(
            ProviderKind.Anthropic,
            "claude-3-7-sonnet-latest",
            thinkingMode: "on",
            reasoningEffort: "medium"));

        request.Thinking.Should().NotBeNull();
        request.Thinking!.Type.Should().Be("enabled");
        request.Thinking.BudgetTokens.Should().Be(8192);
        request.OutputConfig.Should().BeNull();
    }

    [Fact]
    public void BuildChatCompletionRequest_Should_RemoveDeepSeekReasoningContent_FromReplayMessages()
    {
        ConversationProviderRequest request = new(
            new AgentProviderProfile(ProviderKind.DeepSeek, null),
            "test-key",
            "deepseek-reasoner",
            [
                ConversationRequestMessage.AssistantMessage(
                    "I inspected the file.",
                    reasoningContent: "Internal reasoning that should not be replayed."),
                ConversationRequestMessage.User("Continue.")
            ],
            null,
            [],
            ThinkingMode: "on");

        OpenAiChatCompletionRequest payload = _sut.BuildChatCompletionRequest(request);

        payload.Messages[0].Role.Should().Be("assistant");
        payload.Messages[0].ReasoningContent.Should().BeNull();
    }

    [Fact]
    public void BuildChatCompletionRequest_Should_MapGeminiThinkingBudget_ForGemini25Models()
    {
        OpenAiChatCompletionRequest request = _sut.BuildChatCompletionRequest(CreateRequest(
            ProviderKind.GoogleAiStudio,
            "gemini-2.5-pro",
            thinkingMode: "on",
            reasoningEffort: "high",
            showThinking: false));

        request.ThinkingConfig.Should().NotBeNull();
        request.ThinkingConfig!.ThinkingBudget.Should().Be(8192);
        request.ReasoningEffort.Should().BeNull();
    }

    [Fact]
    public void BuildChatCompletionRequest_Should_MapOpenRouterUnifiedReasoningObject()
    {
        OpenAiChatCompletionRequest request = _sut.BuildChatCompletionRequest(CreateRequest(
            ProviderKind.OpenRouter,
            "openai/gpt-5.4",
            thinkingMode: "on",
            reasoningEffort: "max"));

        request.ReasoningEffort.Should().BeNull();
        request.Reasoning.Should().NotBeNull();
        request.Reasoning!.Effort.Should().Be("xhigh");
        request.Reasoning.Exclude.Should().BeFalse();
    }

    private static ConversationProviderRequest CreateRequest(
        ProviderKind providerKind,
        string modelId,
        string? thinkingMode = null,
        string? reasoningEffort = null,
        bool showThinking = true)
    {
        return new ConversationProviderRequest(
            new AgentProviderProfile(providerKind, null),
            "test-key",
            modelId,
            [ConversationRequestMessage.User("Hello.")],
            null,
            [],
            ReasoningEffort: reasoningEffort,
            ThinkingMode: thinkingMode,
            ShowThinking: showThinking);
    }
}
