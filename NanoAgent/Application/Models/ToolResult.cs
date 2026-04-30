using NanoAgent.Application.Utilities;
using System.Text.Json;

namespace NanoAgent.Application.Models;

public sealed class ToolResult
{
    public ToolResult(
        ToolResultStatus status,
        string message,
        string jsonResult,
        ToolRenderPayload? renderPayload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonResult);

        Status = status;
        EnsureValidJson(jsonResult);
        string redactedJsonResult = SecretRedactor.Redact(jsonResult.Trim());
        EnsureValidJson(redactedJsonResult);
        Message = SecretRedactor.Redact(message.Trim());
        JsonResult = redactedJsonResult;
        RenderPayload = renderPayload;
    }

    public bool IsSuccess => Status == ToolResultStatus.Success;

    public string JsonResult { get; }

    public string Message { get; }

    public ToolRenderPayload? RenderPayload { get; }

    public ToolResultStatus Status { get; }

    public static ToolResult ExecutionError(
        string message,
        string jsonResult,
        ToolRenderPayload? renderPayload = null)
    {
        return new ToolResult(
            ToolResultStatus.ExecutionError,
            message,
            jsonResult,
            renderPayload);
    }

    public static ToolResult InvalidArguments(
        string message,
        string jsonResult,
        ToolRenderPayload? renderPayload = null)
    {
        return new ToolResult(
            ToolResultStatus.InvalidArguments,
            message,
            jsonResult,
            renderPayload);
    }

    public static ToolResult NotFound(
        string message,
        string jsonResult,
        ToolRenderPayload? renderPayload = null)
    {
        return new ToolResult(
            ToolResultStatus.NotFound,
            message,
            jsonResult,
            renderPayload);
    }

    public static ToolResult PermissionDenied(
        string message,
        string jsonResult,
        ToolRenderPayload? renderPayload = null)
    {
        return new ToolResult(
            ToolResultStatus.PermissionDenied,
            message,
            jsonResult,
            renderPayload);
    }

    public static ToolResult Success(
        string message,
        string jsonResult,
        ToolRenderPayload? renderPayload = null)
    {
        return new ToolResult(
            ToolResultStatus.Success,
            message,
            jsonResult,
            renderPayload);
    }

    private static void EnsureValidJson(string jsonResult)
    {
        using JsonDocument _ = JsonDocument.Parse(jsonResult);
    }
}
