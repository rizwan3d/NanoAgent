namespace StemCode.Application.Models;

public sealed class ToolInvocationResult
{
    public ToolInvocationResult(
        string toolCallId,
        string toolName,
        ToolResult result,
        bool toolNameRecognized = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(result);

        ToolCallId = toolCallId.Trim();
        ToolName = toolName.Trim();
        Result = result;
        ToolNameRecognized = toolNameRecognized;
    }

    public ToolResult Result { get; }

    public string ToolCallId { get; }

    public string ToolName { get; }

    /// <summary>
    /// Indicates whether <see cref="ToolName"/> resolved to a tool registered in this agent.
    /// When false, the name came from the model/provider and does not correspond to any real
    /// tool (for example a malformed or hallucinated name), so telemetry should not record it verbatim.
    /// </summary>
    public bool ToolNameRecognized { get; }

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
