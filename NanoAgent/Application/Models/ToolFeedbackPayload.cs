using System.Text.Json;

namespace NanoAgent.Application.Models;

public sealed class ToolFeedbackPayload
{
    public ToolFeedbackPayload(
        string toolName,
        ToolResultStatus status,
        bool isSuccess,
        int consecutiveFailureCount,
        string message,
        JsonElement data,
        ToolRenderPayload? render = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ToolName = toolName.Trim();
        Status = status;
        IsSuccess = isSuccess;
        ConsecutiveFailureCount = Math.Max(0, consecutiveFailureCount);
        Message = message.Trim();
        Data = data.Clone();
        Render = render;
    }

    public int ConsecutiveFailureCount { get; }

    public JsonElement Data { get; }

    public bool IsSuccess { get; }

    public string Message { get; }

    public ToolRenderPayload? Render { get; }

    public ToolResultStatus Status { get; }

    public string ToolName { get; }
}
