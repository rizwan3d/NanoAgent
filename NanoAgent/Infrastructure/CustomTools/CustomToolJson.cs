using System.Text;
using System.Text.Json;
using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.CustomTools;

internal static class CustomToolJson
{
    public static string CreatePermissionRequirements(
        string configuredToolName,
        ToolApprovalMode approvalMode)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("approvalMode", approvalMode.ToString());
            writer.WritePropertyName("toolTags");
            writer.WriteStartArray();
            writer.WriteStringValue("custom");
            writer.WriteStringValue("custom_tool");
            writer.WriteStringValue($"custom:{configuredToolName}");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string CreateToolInput(
        ToolExecutionContext context,
        string configuredToolName)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("toolName", context.ToolName);
            writer.WriteString("configuredName", configuredToolName);
            writer.WritePropertyName("arguments");
            context.Arguments.WriteTo(writer);
            writer.WritePropertyName("session");
            writer.WriteStartObject();
            writer.WriteString("id", context.Session.SessionId);
            writer.WriteString("workspacePath", context.Session.WorkspacePath);
            writer.WriteString("workingDirectory", context.Session.WorkingDirectory);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static JsonElement CreateProcessFailurePayload(
        string configuredToolName,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("tool", configuredToolName);
            writer.WriteNumber("exitCode", exitCode);
            writer.WriteString("stdout", standardOutput);
            writer.WriteString("stderr", standardError);
            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    public static JsonElement CreateTextPayload(
        string configuredToolName,
        string text)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("tool", configuredToolName);
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    public static JsonElement CreateProtocolPayload(
        string configuredToolName,
        JsonElement result)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("tool", configuredToolName);
            writer.WritePropertyName("result");
            result.WriteTo(writer);
            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}
