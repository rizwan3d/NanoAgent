namespace StemCode.Application.Models;

public sealed class PermissionEvaluationContext
{
    public PermissionEvaluationContext(
        ToolExecutionContext toolExecutionContext,
        bool approvalGranted = false)
    {
        ArgumentNullException.ThrowIfNull(toolExecutionContext);

        ToolExecutionContext = toolExecutionContext;
        ApprovalGranted = approvalGranted;
    }

    public bool ApprovalGranted { get; }

    public ToolExecutionContext ToolExecutionContext { get; }
}
