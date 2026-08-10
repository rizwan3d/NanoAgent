namespace StemCode.Application.Models;

public sealed class PendingExecutionPlan
{
    public PendingExecutionPlan(
        string sourceUserInput,
        string planningSummary,
        IReadOnlyList<string> tasks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUserInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(planningSummary);
        ArgumentNullException.ThrowIfNull(tasks);

        SourceUserInput = sourceUserInput.Trim();
        PlanningSummary = planningSummary.Trim();
        Tasks = tasks
            .Where(static task => !string.IsNullOrWhiteSpace(task))
            .Select(static task => task.Trim())
            .ToArray();
    }

    public string PlanningSummary { get; }

    public string SourceUserInput { get; }

    public IReadOnlyList<string> Tasks { get; }
}
