using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class ConversationResponseTests
{
    [Fact]
    public void Should_Store_All_Properties()
    {
        var toolCalls = new List<ConversationToolCall>
        {
            new ConversationToolCall("call_1", "file_read", "{}")
        };

        var response = new ConversationResponse(
            "Hello",
            toolCalls,
            "resp_1",
            100,
            50,
            150,
            10,
            "thinking",
            "{\"details\":true}");

        response.AssistantMessage.Should().Be("Hello");
        response.ToolCalls.Should().BeSameAs(toolCalls);
        response.ResponseId.Should().Be("resp_1");
        response.CompletionTokens.Should().Be(100);
        response.PromptTokens.Should().Be(50);
        response.TotalTokens.Should().Be(150);
        response.CachedPromptTokens.Should().Be(10);
        response.ReasoningContent.Should().Be("thinking");
        response.ReasoningDetailsJson.Should().Be("{\"details\":true}");
    }

    [Fact]
    public void HasToolCalls_Should_Be_False_When_NoToolCalls()
    {
        var response = new ConversationResponse("Hello", [], "resp_1");

        response.HasToolCalls.Should().BeFalse();
    }

    [Fact]
    public void HasToolCalls_Should_Be_True_When_ToolCallsExist()
    {
        var response = new ConversationResponse(
            null,
            [new ConversationToolCall("call_1", "tool", "{}")],
            "resp_1");

        response.HasToolCalls.Should().BeTrue();
    }

    [Fact]
    public void Should_Allow_Null_AssistantMessage()
    {
        var response = new ConversationResponse(null, [], "resp_1");

        response.AssistantMessage.Should().BeNull();
    }
}
