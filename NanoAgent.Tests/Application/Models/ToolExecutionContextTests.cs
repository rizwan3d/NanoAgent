using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Models;

public sealed class ToolExecutionContextTests
{
    [Fact]
    public void Should_Construct_With_ValidArguments()
    {
        using JsonDocument doc = JsonDocument.Parse("""{ "command": "test" }""");
        var session = new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model",
            ["model"]);

        var context = new ToolExecutionContext(
            "call_1",
            "test_tool",
            doc.RootElement.Clone(),
            session,
            ConversationExecutionPhase.Execution);

        context.ToolCallId.Should().Be("call_1");
        context.ToolName.Should().Be("test_tool");
        context.Session.Should().Be(session);
        context.ExecutionPhase.Should().Be(ConversationExecutionPhase.Execution);
    }

    [Fact]
    public void Should_Trim_ToolCallId_And_ToolName()
    {
        using JsonDocument doc = JsonDocument.Parse("{}");
        var session = new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model",
            ["model"]);

        var context = new ToolExecutionContext(
            "  call_1  ",
            "  test_tool  ",
            doc.RootElement.Clone(),
            session);

        context.ToolCallId.Should().Be("call_1");
        context.ToolName.Should().Be("test_tool");
    }

    [Fact]
    public void Should_Default_To_ExecutionPhase()
    {
        using JsonDocument doc = JsonDocument.Parse("{}");
        var session = new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model",
            ["model"]);

        var context = new ToolExecutionContext(
            "call_1",
            "tool",
            doc.RootElement.Clone(),
            session);

        context.ExecutionPhase.Should().Be(ConversationExecutionPhase.Execution);
    }

    [Fact]
    public void Should_Throw_When_ToolCallIdIsEmpty()
    {
        using JsonDocument doc = JsonDocument.Parse("{}");
        var session = new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model", ["model"]);

        Action act = () => new ToolExecutionContext("", "tool", doc.RootElement.Clone(), session);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_ToolNameIsEmpty()
    {
        using JsonDocument doc = JsonDocument.Parse("{}");
        var session = new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model", ["model"]);

        Action act = () => new ToolExecutionContext("call_1", "", doc.RootElement.Clone(), session);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_SessionIsNull()
    {
        using JsonDocument doc = JsonDocument.Parse("{}");
        Action act = () => new ToolExecutionContext("call_1", "tool", doc.RootElement.Clone(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Should_Throw_When_ArgumentsIsNotObject()
    {
        using JsonDocument doc = JsonDocument.Parse("[]");
        var session = new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model", ["model"]);

        Action act = () => new ToolExecutionContext("call_1", "tool", doc.RootElement.Clone(), session);
        act.Should().Throw<ArgumentException>().WithMessage("*JSON object*");
    }
}
