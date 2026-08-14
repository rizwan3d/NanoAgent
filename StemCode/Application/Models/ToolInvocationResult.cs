namespace StemCode.Application.Models;

public sealed class ToolInvocationResult
{
    public ToolInvocationResult(
        string toolCallId,
        string toolName,
        ToolResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(result);

        ToolCallId = toolCallId.Trim();
        ToolName = toolName.Trim();
        Result = result;
    }

    public ToolResult Result { get; }

    public string ToolCallId { get; }

    public string ToolName { get; }

    public string ToDisplayText()
    {
        if (Result.RenderPayload is not null)
        {
            return $"{Result.RenderPayload.Title}{Environment.NewLine}{Result.RenderPayload.Text}";
        }

        string prefix = Result.Status switch
        {
            ToolResultStatus.Success => $"Tool '{ToolName}' completed.",
            ToolResultStatus.NotFound => $"Tool '{ToolName}' was not found.",
            ToolResultStatus.InvalidArguments => $"Tool '{ToolName}' rejected the provided arguments.",
            ToolResultStatus.PermissionDenied => $"Tool '{ToolName}' was denied by the permission policy.",
            _ => $"Tool '{ToolName}' failed."
        };

        return $"{prefix}{Environment.NewLine}{Result.Message}";
    }
}
