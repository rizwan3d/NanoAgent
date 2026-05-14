using FluentAssertions;
using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Formatting;

public sealed class PlanOutputFormatterTests
{
    private readonly PlanOutputFormatter _sut = new();

    [Fact]
    public void Format_Should_ReturnProgressSummary()
    {
        var progress = new ExecutionPlanProgress(
            ["Task 1", "Task 2", "Task 3"],
            completedTaskCount: 2);

        string result = _sut.Format(progress);

        result.Should().Contain("Plan progress: 2/3");
        result.Should().Contain("✓ Task 1");
        result.Should().Contain("✓ Task 2");
        result.Should().Contain("☐ Task 3");
    }

    [Fact]
    public void Format_Should_ReturnAllComplete_When_AllTasksDone()
    {
        var progress = new ExecutionPlanProgress(
            ["A", "B", "C"],
            completedTaskCount: 3);

        string result = _sut.Format(progress);

        result.Should().Contain("Plan progress: 3/3");
        result.Should().Contain("✓ A");
        result.Should().Contain("✓ B");
        result.Should().Contain("✓ C");
    }

    [Fact]
    public void Format_Should_ReturnAllPending_When_NoTasksCompleted()
    {
        var progress = new ExecutionPlanProgress(
            ["X", "Y"],
            completedTaskCount: 0);

        string result = _sut.Format(progress);

        result.Should().Contain("Plan progress: 0/2");
        result.Should().Contain("☐ X");
        result.Should().Contain("☐ Y");
    }

    [Fact]
    public void Format_Should_ReturnSimpleMessage_When_TasksEmpty()
    {
        var progress = new ExecutionPlanProgress(
            [],
            completedTaskCount: 0);

        string result = _sut.Format(progress);

        result.Should().Be("Plan updated.");
    }

    [Fact]
    public void Format_Should_Throw_When_ProgressIsNull()
    {
        Action act = () => _sut.Format(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
