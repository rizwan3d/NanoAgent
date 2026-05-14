using FluentAssertions;
using NanoAgent.Application.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Models;

public sealed class PermissionEvaluationContextTests
{
    [Fact]
    public void Should_Construct_With_Defaults()
    {
        var toolContext = CreateToolContext();
        var context = new PermissionEvaluationContext(toolContext);

        context.ToolExecutionContext.Should().Be(toolContext);
        context.ApprovalGranted.Should().BeFalse();
    }

    [Fact]
    public void Should_Construct_With_ApprovalGranted()
    {
        var toolContext = CreateToolContext();
        var context = new PermissionEvaluationContext(toolContext, approvalGranted: true);

        context.ApprovalGranted.Should().BeTrue();
    }

    [Fact]
    public void Should_Throw_When_ToolContextIsNull()
    {
        Action act = () => new PermissionEvaluationContext(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static ToolExecutionContext CreateToolContext()
    {
        using JsonDocument doc = JsonDocument.Parse("{}");
        return new ToolExecutionContext(
            "call_1",
            "test",
            doc.RootElement.Clone(),
            new ReplSessionContext(
                new NanoAgent.Domain.Models.AgentProviderProfile(
                    NanoAgent.Domain.Models.ProviderKind.OpenAi, null),
                "model",
                ["model"]));
    }
}
