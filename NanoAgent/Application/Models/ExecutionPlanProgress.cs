namespace NanoAgent.Application.Models;

public sealed class ExecutionPlanProgress
{
    public ExecutionPlanProgress(
        IReadOnlyList<string> tasks,
        int completedTaskCount)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        string[] normalizedTasks = tasks
            .Where(static task => !string.IsNullOrWhiteSpace(task))
            .Select(static task => task.Trim())
            .ToArray();

        if (completedTaskCount < 0 || completedTaskCount > normalizedTasks.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(completedTaskCount));
        }

        Tasks = normalizedTasks;
        CompletedTaskCount = completedTaskCount;
    }

    public int CompletedTaskCount { get; }

    public int CurrentTaskCount => CurrentTaskIndex >= 0 ? 1 : 0;

    public int CurrentTaskIndex => CompletedTaskCount < Tasks.Count
        ? CompletedTaskCount
        : -1;

    public int RemainingTaskCount => Math.Max(
        0,
        Tasks.Count - CompletedTaskCount - CurrentTaskCount);

    public IReadOnlyList<string> Tasks { get; }
}
