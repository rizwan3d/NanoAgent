using NanoAgent.Application.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Mcp;

internal static class McpJson
{
    public const string ProtocolVersion = "2025-06-18";

    private const int MaxRenderTextCharacters = 8_000;

    public static string BuildRequest(
        int id,
        string method,
        Action<Utf8JsonWriter>? writeParams = null)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            if (writeParams is not null)
            {
                writer.WritePropertyName("params");
                writeParams(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string BuildNotification(
        string method,
        Action<Utf8JsonWriter>? writeParams = null)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
            if (writeParams is not null)
            {
                writer.WritePropertyName("params");
                writeParams(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string BuildMethodNotFoundResponse(JsonElement id)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", -32601);
            writer.WriteString("message", "Method not found.");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void WriteInitializeParams(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("protocolVersion", ProtocolVersion);
        writer.WritePropertyName("capabilities");
        writer.WriteStartObject();
        writer.WriteEndObject();
        writer.WritePropertyName("clientInfo");
        writer.WriteStartObject();
        writer.WriteString("name", "NanoAgent");
        writer.WriteString("version", "1.0");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    public static void WriteListToolsParams(string? cursor, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            writer.WriteString("cursor", cursor);
        }

        writer.WriteEndObject();
    }

    public static void WriteCallToolParams(
        string toolName,
        JsonElement arguments,
        Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("name", toolName);
        writer.WritePropertyName("arguments");
        arguments.WriteTo(writer);
        writer.WriteEndObject();
    }

    public static IReadOnlyList<McpRemoteTool> ParseTools(JsonElement result)
    {
        if (!result.TryGetProperty("tools", out JsonElement toolsElement) ||
            toolsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<McpRemoteTool> tools = [];
        foreach (JsonElement toolElement in toolsElement.EnumerateArray())
        {
            if (toolElement.ValueKind != JsonValueKind.Object ||
                !toolElement.TryGetProperty("name", out JsonElement nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                continue;
            }

            string description = toolElement.TryGetProperty("description", out JsonElement descriptionElement) &&
                                 descriptionElement.ValueKind == JsonValueKind.String
                ? descriptionElement.GetString() ?? string.Empty
                : string.Empty;
            JsonElement schema = toolElement.TryGetProperty("inputSchema", out JsonElement schemaElement) &&
                                 schemaElement.ValueKind == JsonValueKind.Object
                ? schemaElement.Clone()
                : CreateDefaultSchema();

            tools.Add(new McpRemoteTool(
                nameElement.GetString()!.Trim(),
                description.Trim(),
                schema));
        }

        return tools;
    }

    public static string? GetNextCursor(JsonElement result)
    {
        return result.TryGetProperty("nextCursor", out JsonElement cursorElement) &&
               cursorElement.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(cursorElement.GetString())
            ? cursorElement.GetString()
            : null;
    }

    public static McpCallToolResult ParseCallToolResult(JsonElement result)
    {
        bool isError = result.TryGetProperty("isError", out JsonElement isErrorElement) &&
                       isErrorElement.ValueKind is JsonValueKind.True;

        return new McpCallToolResult(
            isError,
            result.Clone(),
            ExtractRenderText(result));
    }

    public static string CreatePermissionRequirements(
        string serverName,
        string remoteToolName,
        ToolApprovalMode approvalMode)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("approvalMode", approvalMode.ToString());
            writer.WritePropertyName("toolTags");
            writer.WriteStartArray();
            writer.WriteStringValue("mcp");
            writer.WriteStringValue($"mcp:{serverName}");
            writer.WriteStringValue($"mcp:{serverName}:{remoteToolName}");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static JsonElement CreateToolResultPayload(
        string serverName,
        string remoteToolName,
        bool isError,
        JsonElement result)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("server", serverName);
            writer.WriteString("tool", remoteToolName);
            writer.WriteBoolean("isError", isError);
            writer.WritePropertyName("result");
            result.WriteTo(writer);
            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    public static JsonElement CreateDefaultSchema()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{ "type": "object", "properties": {}, "additionalProperties": true }""");
        return document.RootElement.Clone();
    }

    public static string CreateShortHash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    public static string GetJsonRpcErrorMessage(JsonElement response)
    {
        if (!response.TryGetProperty("error", out JsonElement errorElement))
        {
            return "The MCP server returned a JSON-RPC error.";
        }

        string? message = errorElement.TryGetProperty("message", out JsonElement messageElement) &&
                          messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString()
            : null;
        int? code = errorElement.TryGetProperty("code", out JsonElement codeElement) &&
                    codeElement.TryGetInt32(out int parsedCode)
            ? parsedCode
            : null;

        if (code is null)
        {
            return string.IsNullOrWhiteSpace(message)
                ? "The MCP server returned a JSON-RPC error."
                : message!;
        }

        return string.IsNullOrWhiteSpace(message)
            ? $"The MCP server returned JSON-RPC error {code.Value}."
            : $"The MCP server returned JSON-RPC error {code.Value}: {message}";
    }

    private static string ExtractRenderText(JsonElement result)
    {
        if (!result.TryGetProperty("content", out JsonElement contentElement) ||
            contentElement.ValueKind != JsonValueKind.Array)
        {
            return TrimRenderText(result.GetRawText());
        }

        List<string> lines = [];
        foreach (JsonElement item in contentElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("type", out JsonElement typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                lines.Add(item.GetRawText());
                continue;
            }

            string? type = typeElement.GetString();
            if (string.Equals(type, "text", StringComparison.Ordinal) &&
                item.TryGetProperty("text", out JsonElement textElement) &&
                textElement.ValueKind == JsonValueKind.String)
            {
                lines.Add(textElement.GetString() ?? string.Empty);
                continue;
            }

            if (string.Equals(type, "image", StringComparison.Ordinal))
            {
                string mimeType = item.TryGetProperty("mimeType", out JsonElement mimeTypeElement) &&
                                  mimeTypeElement.ValueKind == JsonValueKind.String
                    ? mimeTypeElement.GetString() ?? "image"
                    : "image";
                lines.Add($"[{mimeType} image returned by MCP server]");
                continue;
            }

            lines.Add(item.GetRawText());
        }

        return TrimRenderText(string.Join(Environment.NewLine, lines));
    }

    private static string TrimRenderText(string value)
    {
        string normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length <= MaxRenderTextCharacters)
        {
            return normalized;
        }

        return normalized[..MaxRenderTextCharacters] + "...";
    }
}
