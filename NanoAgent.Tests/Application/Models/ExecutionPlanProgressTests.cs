using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class ExecutionPlanProgressTests
{
    [Fact]
    public void Should_Construct_With_ValidArguments()
    {
        var progress = new ExecutionPlanProgress(
            ["task 1", "task 2", "task 3"],
            completedTaskCount: 1);

        progress.Tasks.Should().BeEquivalentTo(["task 1", "task 2", "task 3"], opts => opts.WithStrictOrdering());
        progress.CompletedTaskCount.Should().Be(1);
    }

    [Fact]
    public void Should_Filter_Empty_Tasks()
    {
        var progress = new ExecutionPlanProgress(
            ["task 1", "", "  ", "task 2"],
            completedTaskCount: 1);

        progress.Tasks.Should().BeEquivalentTo(["task 1", "task 2"]);
    }

    [Fact]
    public void Should_Compute_CurrentTaskIndex_When_NotAllTasksCompleted()
    {
        var progress = new ExecutionPlanProgress(
            ["a", "b", "c"],
            completedTaskCount: 1);

        progress.CurrentTaskIndex.Should().Be(1); // 0-based index of the next task
        progress.CurrentTaskCount.Should().Be(1);
    }

    [Fact]
    public void Should_Set_CurrentTaskIndex_To_MinusOne_When_AllTasksCompleted()
    {
        var progress = new ExecutionPlanProgress(
            ["a", "b"],
            completedTaskCount: 2);

        progress.CurrentTaskIndex.Should().Be(-1);
        progress.CurrentTaskCount.Should().Be(0);
    }

    [Fact]
    public void Should_Compute_CurrentTaskIndex_When_NoTasks()
    {
        var progress = new ExecutionPlanProgress(
            [],
            completedTaskCount: 0);

        progress.CurrentTaskIndex.Should().Be(-1);
        progress.CurrentTaskCount.Should().Be(0);
    }

    [Fact]
    public void Should_Compute_RemainingTaskCount()
    {
        var progress = new ExecutionPlanProgress(
            ["a", "b", "c", "d"],
            completedTaskCount: 2);

        // remaining = 4 - 2 - 1 (current) = 1
        progress.RemainingTaskCount.Should().Be(1);
    }

    [Fact]
    public void Should_Compute_RemainingTaskCount_When_NoTasks()
    {
        var progress = new ExecutionPlanProgress([], completedTaskCount: 0);

        progress.RemainingTaskCount.Should().Be(0);
    }

    [Fact]
    public void Should_Set_RemainingTaskCount_To_Zero_When_AllCompleted()
    {
        var progress = new ExecutionPlanProgress(
            ["a", "b"],
            completedTaskCount: 2);

        progress.RemainingTaskCount.Should().Be(0);
    }

    [Fact]
    public void Should_Throw_When_TasksIsNull()
    {
        Action act = () => new ExecutionPlanProgress(null!, 0);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Should_Throw_When_CompletedTaskCount_IsNegative()
    {
        Action act = () => new ExecutionPlanProgress(["a"], -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Should_Throw_When_CompletedTaskCount_ExceedsTaskCount()
    {
        Action act = () => new ExecutionPlanProgress(["a"], 5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Should_Handle_Initial_Progress()
    {
        var progress = new ExecutionPlanProgress(
            ["task 1", "task 2"],
            completedTaskCount: 0);

        progress.CompletedTaskCount.Should().Be(0);
        progress.CurrentTaskIndex.Should().Be(0);
        progress.CurrentTaskCount.Should().Be(1);
        progress.RemainingTaskCount.Should().Be(1); // 2 - 0 - 1 = 1
    }
}
