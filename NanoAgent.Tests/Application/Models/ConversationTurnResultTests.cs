using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class ConversationTurnResultTests
{
    [Fact]
    public void Constructor_Should_Set_Properties()
    {
        var result = new ConversationTurnResult(
            ConversationTurnResultKind.AssistantMessage,
            "Hello",
            null,
            null);

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ResponseText.Should().Be("Hello");
        result.ToolExecutionResult.Should().BeNull();
        result.Metrics.Should().BeNull();
        result.ReasoningText.Should().BeNull();
    }

    [Fact]
    public void Constructor_Should_Trim_ResponseText()
    {
        var result = new ConversationTurnResult(
            ConversationTurnResultKind.AssistantMessage,
            "  Hello  ",
            null,
            null);

        result.ResponseText.Should().Be("Hello");
    }

    [Fact]
    public void Constructor_WithSingleArgument_Should_CreateAssistantMessage()
    {
        var result = new ConversationTurnResult("Hello");

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ResponseText.Should().Be("Hello");
    }

    [Fact]
    public void Constructor_WithKindAndResponse_Should_CreateWithKind()
    {
        var result = new ConversationTurnResult(ConversationTurnResultKind.ToolExecution, "Done");

        result.Kind.Should().Be(ConversationTurnResultKind.ToolExecution);
        result.ResponseText.Should().Be("Done");
    }

    [Fact]
    public void Static_AssistantMessage_Should_CreateResult()
    {
        var result = ConversationTurnResult.AssistantMessage("Hello");

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ResponseText.Should().Be("Hello");
    }

    [Fact]
    public void Static_AssistantMessage_WithMetrics_Should_CreateResult()
    {
        var metrics = new ConversationTurnMetrics(
            TimeSpan.FromSeconds(5), 100);
        var result = ConversationTurnResult.AssistantMessage("Hello", metrics);

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.Metrics.Should().Be(metrics);
    }

    [Fact]
    public void Static_AssistantMessage_WithToolResultAndMetrics_Should_CreateResult()
    {
        var batchResult = new ToolExecutionBatchResult(Array.Empty<ToolInvocationResult>());
        var metrics = new ConversationTurnMetrics(
            TimeSpan.FromSeconds(5), 100);
        var result = ConversationTurnResult.AssistantMessage("Hello", batchResult, metrics);

        result.Kind.Should().Be(ConversationTurnResultKind.AssistantMessage);
        result.ToolExecutionResult.Should().Be(batchResult);
    }

    [Fact]
    public void Static_AssistantMessage_WithReasoning_Should_CreateResult()
    {
        var result = ConversationTurnResult.AssistantMessage("Hello", null, null, "thinking");

        result.ReasoningText.Should().Be("thinking");
    }

    [Fact]
    public void Static_AssistantMessage_Should_TrimReasoning()
    {
        var result = ConversationTurnResult.AssistantMessage("Hello", null, null, "  thinking  ");

        result.ReasoningText.Should().Be("thinking");
    }

    [Fact]
    public void Static_ToolExecution_WithText_Should_CreateResult()
    {
        var result = ConversationTurnResult.ToolExecution("Done");

        result.Kind.Should().Be(ConversationTurnResultKind.ToolExecution);
        result.ResponseText.Should().Be("Done");
    }

    [Fact]
    public void Static_ToolExecution_WithBatchResult_Should_CreateResult()
    {
        var batchResult = new ToolExecutionBatchResult(Array.Empty<ToolInvocationResult>());
        var result = ConversationTurnResult.ToolExecution(batchResult);

        result.Kind.Should().Be(ConversationTurnResultKind.ToolExecution);
        result.ResponseText.Should().Be("The provider requested tool execution, but no tool calls were included.");
    }

    [Fact]
    public void Static_ToolExecution_WithBatchResult_Should_Throw_When_Null()
    {
        Action act = () => ConversationTurnResult.ToolExecution((ToolExecutionBatchResult)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_Should_Throw_When_ResponseTextIsEmpty()
    {
        Action act = () => new ConversationTurnResult("");

        act.Should().Throw<ArgumentException>();
    }
}
