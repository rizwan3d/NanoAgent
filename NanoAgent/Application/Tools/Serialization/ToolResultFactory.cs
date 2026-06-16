using NanoAgent.Application.Models;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NanoAgent.Application.Tools.Serialization;

internal static class ToolResultFactory
{
    private static readonly ToolJsonContext RelaxedJsonContext = new(
        new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

    public static ToolResult ExecutionError(
        string code,
        string message,
        ToolRenderPayload? renderPayload = null)
    {
        return ToolResult.ExecutionError(
            message,
            Serialize(new ToolErrorPayload(code, message), ToolJsonContext.Default.ToolErrorPayload),
            renderPayload);
    }

    public static ToolResult InvalidArguments(
        string code,
        string message,
        ToolRenderPayload? renderPayload = null)
    {
        return ToolResult.InvalidArguments(
            message,
            Serialize(new ToolErrorPayload(code, message), ToolJsonContext.Default.ToolErrorPayload),
            renderPayload);
    }

    public static ToolResult NotFound(
        string code,
        string message,
        ToolRenderPayload? renderPayload = null)
    {
        return ToolResult.NotFound(
            message,
            Serialize(new ToolErrorPayload(code, message), ToolJsonContext.Default.ToolErrorPayload),
            renderPayload);
    }

    public static ToolResult PermissionDenied(
        string code,
        string message,
        ToolRenderPayload? renderPayload = null)
    {
        return ToolResult.PermissionDenied(
            message,
            Serialize(new ToolErrorPayload(code, message), ToolJsonContext.Default.ToolErrorPayload),
            renderPayload);
    }

    public static ToolResult Success<TPayload>(
        string message,
        TPayload payload,
        JsonTypeInfo<TPayload> typeInfo,
        ToolRenderPayload? renderPayload = null)
    {
        return ToolResult.Success(
            message,
            Serialize(payload, typeInfo),
            renderPayload);
    }

    private static string Serialize<TPayload>(
        TPayload payload,
        JsonTypeInfo<TPayload> typeInfo)
    {
        JsonTypeInfo<TPayload> relaxedTypeInfo =
            RelaxedJsonContext.GetTypeInfo(typeInfo.Type) as JsonTypeInfo<TPayload> ?? typeInfo;

        return JsonSerializer.Serialize(payload, relaxedTypeInfo);
    }
}
