namespace StemCode.Application.Models;

public sealed class ToolExecutionBatchResult
{
    public ToolExecutionBatchResult(IReadOnlyList<ToolInvocationResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        Results = results;
    }

    public bool HasFailures => Results.Any(static result => !result.Result.IsSuccess);

    public IReadOnlyList<ToolInvocationResult> Results { get; }

    public string ToDisplayText()
    {
        if (Results.Count == 0)
        {
            return "The provider requested tool execution, but no tool calls were included.";
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            Results.Select(static result => result.ToDisplayText()));
    }
}
