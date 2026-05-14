using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class PendingExecutionPlanTests
{
    [Fact]
    public void Should_Construct_WithValidArguments()
    {
        var plan = new PendingExecutionPlan(
            "user input",
            "plan summary",
            ["task 1", "task 2", "task 3"]);

        plan.SourceUserInput.Should().Be("user input");
        plan.PlanningSummary.Should().Be("plan summary");
        plan.Tasks.Should().BeEquivalentTo(["task 1", "task 2", "task 3"]);
    }

    [Fact]
    public void Should_Trim_Values()
    {
        var plan = new PendingExecutionPlan(
            "  user input  ",
            "  plan summary  ",
            ["  task 1  ", "  task 2  "]);

        plan.SourceUserInput.Should().Be("user input");
        plan.PlanningSummary.Should().Be("plan summary");
        plan.Tasks.Should().BeEquivalentTo(["task 1", "task 2"]);
    }

    [Fact]
    public void Should_Filter_Empty_Tasks()
    {
        var plan = new PendingExecutionPlan(
            "user input",
            "plan summary",
            ["task 1", "", "  ", "task 2"]);

        plan.Tasks.Should().BeEquivalentTo(["task 1", "task 2"]);
    }

    [Fact]
    public void Should_Throw_When_SourceUserInputIsNullOrWhiteSpace()
    {
        Action act = () => new PendingExecutionPlan("", "summary", ["task"]);
        act.Should().Throw<ArgumentException>();

        act = () => new PendingExecutionPlan("  ", "summary", ["task"]);
        act.Should().Throw<ArgumentException>();

        act = () => new PendingExecutionPlan(null!, "summary", ["task"]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_PlanningSummaryIsNullOrWhiteSpace()
    {
        Action act = () => new PendingExecutionPlan("input", "", ["task"]);
        act.Should().Throw<ArgumentException>();

        act = () => new PendingExecutionPlan("input", null!, ["task"]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_TasksIsNull()
    {
        Action act = () => new PendingExecutionPlan("input", "summary", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
