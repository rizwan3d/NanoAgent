using NanoAgent.Application.Models;

namespace NanoAgent.Application.Formatting;

public interface IPlanOutputFormatter
{
    string Format(ExecutionPlanProgress progress);
}

public sealed class PlanOutputFormatter : IPlanOutputFormatter
{
    private const string CompleteMarker = "\u2713";
    private const string PendingMarker = "\u2610";

    public string Format(ExecutionPlanProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (progress.Tasks.Count == 0)
        {
            return "Plan updated.";
        }

        List<string> lines =
        [
            $"Plan progress: {progress.CompletedTaskCount}/{progress.Tasks.Count}"
        ];

        for (int index = 0; index < progress.Tasks.Count; index++)
        {
            string marker = index < progress.CompletedTaskCount
                ? CompleteMarker
                : PendingMarker;

            lines.Add($"{marker} {progress.Tasks[index]}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
