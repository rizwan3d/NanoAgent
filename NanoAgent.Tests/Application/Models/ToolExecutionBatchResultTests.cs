using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class ToolExecutionBatchResultTests
{
    [Fact]
    public void Constructor_Should_Set_Results()
    {
        var results = Array.Empty<ToolInvocationResult>();
        var batchResult = new ToolExecutionBatchResult(results);

        batchResult.Results.Should().BeSameAs(results);
    }

    [Fact]
    public void HasFailures_Should_Be_False_When_NoResults()
    {
        var batchResult = new ToolExecutionBatchResult(Array.Empty<ToolInvocationResult>());

        batchResult.HasFailures.Should().BeFalse();
    }

    [Fact]
    public void ToDisplayText_Should_Return_Default_When_NoResults()
    {
        var batchResult = new ToolExecutionBatchResult(Array.Empty<ToolInvocationResult>());

        string text = batchResult.ToDisplayText();

        text.Should().Be("The provider requested tool execution, but no tool calls were included.");
    }

    [Fact]
    public void Should_Throw_When_ResultsIsNull()
    {
        Action act = () => new ToolExecutionBatchResult(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
