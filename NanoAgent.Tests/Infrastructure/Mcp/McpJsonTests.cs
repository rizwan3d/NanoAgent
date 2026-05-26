using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Mcp;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.Mcp;

public sealed class McpJsonTests
{
    [Fact]
    public void BuildRequest_ShouldIncludeIdMethodAndParams()
    {
        string json = McpJson.BuildRequest(
            7,
            "tools/call",
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("name", "grep");
                writer.WriteEndObject();
            });

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("id").GetInt32().Should().Be(7);
        root.GetProperty("method").GetString().Should().Be("tools/call");
        root.GetProperty("params").GetProperty("name").GetString().Should().Be("grep");
    }

    [Fact]
    public void BuildNotification_ShouldOmitId_AndAllowNullParams()
    {
        string json = McpJson.BuildNotification("notifications/cancelled");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        root.TryGetProperty("id", out _).Should().BeFalse();
        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("method").GetString().Should().Be("notifications/cancelled");
    }

    [Fact]
    public void BuildMethodNotFoundResponse_ShouldPreserveIdAndErrorPayload()
    {
        using JsonDocument idDocument = JsonDocument.Parse("\"abc-123\"");

        string json = McpJson.BuildMethodNotFoundResponse(idDocument.RootElement);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        root.GetProperty("id").GetString().Should().Be("abc-123");
        root.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);
        root.GetProperty("error").GetProperty("message").GetString().Should().Be("Method not found.");
    }

    [Fact]
    public void ParameterWriters_ShouldEmitExpectedPayloads()
    {
        string initialize = McpJson.BuildRequest(1, "initialize", McpJson.WriteInitializeParams);
        string noCursor = McpJson.BuildRequest(
            2,
            "tools/list",
            writer => McpJson.WriteListToolsParams(null, writer));
        string withCursor = McpJson.BuildRequest(
            3,
            "tools/list",
            writer => McpJson.WriteListToolsParams("cursor-1", writer));

        using JsonDocument argsDocument = JsonDocument.Parse("""{ "query": "coverage", "limit": 5 }""");
        string callTool = McpJson.BuildRequest(
            4,
            "tools/call",
            writer => McpJson.WriteCallToolParams("search_files", argsDocument.RootElement, writer));

        using JsonDocument initializeDoc = JsonDocument.Parse(initialize);
        using JsonDocument noCursorDoc = JsonDocument.Parse(noCursor);
        using JsonDocument withCursorDoc = JsonDocument.Parse(withCursor);
        using JsonDocument callToolDoc = JsonDocument.Parse(callTool);

        initializeDoc.RootElement.GetProperty("params").GetProperty("protocolVersion").GetString()
            .Should().Be(McpJson.ProtocolVersion);
        initializeDoc.RootElement.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString()
            .Should().Be("NanoAgent");
        noCursorDoc.RootElement.GetProperty("params").EnumerateObject().Should().BeEmpty();
        withCursorDoc.RootElement.GetProperty("params").GetProperty("cursor").GetString().Should().Be("cursor-1");
        callToolDoc.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("search_files");
        callToolDoc.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("limit").GetInt32()
            .Should().Be(5);
    }

    [Fact]
    public void ParseTools_ShouldSkipInvalidEntries_AndSupplyDefaults()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "tools": [
                {
                  "name": " search_files ",
                  "description": " find files ",
                  "inputSchema": {
                    "type": "object",
                    "properties": {
                      "query": { "type": "string" }
                    }
                  }
                },
                {
                  "name": "list_tools"
                },
                {
                  "description": "missing name"
                },
                {
                  "name": "   "
                }
              ]
            }
            """);

        IReadOnlyList<McpRemoteTool> tools = McpJson.ParseTools(document.RootElement);

        tools.Should().HaveCount(2);
        tools[0].Name.Should().Be("search_files");
        tools[0].Description.Should().Be("find files");
        tools[0].InputSchema.GetProperty("properties").GetProperty("query").GetProperty("type").GetString()
            .Should().Be("string");
        tools[1].Name.Should().Be("list_tools");
        tools[1].Description.Should().BeEmpty();
        tools[1].InputSchema.GetProperty("additionalProperties").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void GetNextCursor_ShouldReturnOnlyNonBlankStringValues()
    {
        using JsonDocument valid = JsonDocument.Parse("""{ "nextCursor": "cursor-2" }""");
        using JsonDocument blank = JsonDocument.Parse("""{ "nextCursor": "   " }""");
        using JsonDocument missing = JsonDocument.Parse("""{ }""");

        McpJson.GetNextCursor(valid.RootElement).Should().Be("cursor-2");
        McpJson.GetNextCursor(blank.RootElement).Should().BeNull();
        McpJson.GetNextCursor(missing.RootElement).Should().BeNull();
    }

    [Fact]
    public void ParseCallToolResult_ShouldExtractRenderTextAcrossContentTypes()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "isError": true,
              "content": [
                { "type": "text", "text": "first line" },
                { "type": "image", "mimeType": "image/png" },
                { "type": "custom", "value": 42 },
                5
              ]
            }
            """);

        McpCallToolResult result = McpJson.ParseCallToolResult(document.RootElement);

        result.IsError.Should().BeTrue();
        result.Result.GetProperty("content").GetArrayLength().Should().Be(4);
        result.RenderText.Should().Contain("first line");
        result.RenderText.Should().Contain("[image/png image returned by MCP server]");
        result.RenderText.Should().Contain("\"type\": \"custom\"");
        result.RenderText.Should().Contain("\"value\": 42");
        result.RenderText.Should().Contain("5");
    }

    [Fact]
    public void ParseCallToolResult_ShouldFallBackToRawJsonAndTrimLongOutput()
    {
        string longText = new('a', 8_100);
        using JsonDocument document = JsonDocument.Parse($$"""
            {
              "value": "{{longText}}"
            }
            """);

        McpCallToolResult result = McpJson.ParseCallToolResult(document.RootElement);

        result.RenderText.Length.Should().Be(8_003);
        result.RenderText.Should().EndWith("...");
    }

    [Fact]
    public void CreatePermissionRequirements_ShouldIncludeApprovalModeAndToolTags()
    {
        string json = McpJson.CreatePermissionRequirements(
            "github",
            "list_prs",
            ToolApprovalMode.RequireApproval);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        root.GetProperty("approvalMode").GetString().Should().Be(nameof(ToolApprovalMode.RequireApproval));
        root.GetProperty("toolTags").EnumerateArray().Select(static item => item.GetString()).Should().ContainInOrder(
        [
            "mcp",
            "mcp:github",
            "mcp:github:list_prs"
        ]);
    }

    [Fact]
    public void CreateToolResultPayload_ShouldWrapMetadataAndCloneResult()
    {
        using JsonDocument resultDocument = JsonDocument.Parse("""{ "answer": 42, "nested": { "ok": true } }""");

        JsonElement payload = McpJson.CreateToolResultPayload(
            "filesystem",
            "read_file",
            isError: false,
            resultDocument.RootElement);

        payload.GetProperty("server").GetString().Should().Be("filesystem");
        payload.GetProperty("tool").GetString().Should().Be("read_file");
        payload.GetProperty("isError").GetBoolean().Should().BeFalse();
        payload.GetProperty("result").GetProperty("answer").GetInt32().Should().Be(42);
        payload.GetProperty("result").GetProperty("nested").GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CreateDefaultSchema_ShouldReturnReusableObjectSchema()
    {
        JsonElement schema = McpJson.CreateDefaultSchema();

        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("properties").ValueKind.Should().Be(JsonValueKind.Object);
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CreateShortHash_ShouldBeStableAndEightHexCharacters()
    {
        string hash = McpJson.CreateShortHash("nanoagent");
        string secondHash = McpJson.CreateShortHash("nanoagent");

        hash.Should().Be("38c142fa");
        secondHash.Should().Be(hash);
        hash.Should().MatchRegex("^[0-9a-f]{8}$");
    }

    [Fact]
    public void GetJsonRpcErrorMessage_ShouldHandleMissingMessageAndCodeVariants()
    {
        using JsonDocument missingError = JsonDocument.Parse("""{ }""");
        using JsonDocument messageOnly = JsonDocument.Parse("""{ "error": { "message": "bad request" } }""");
        using JsonDocument codeOnly = JsonDocument.Parse("""{ "error": { "code": -32001 } }""");
        using JsonDocument codeAndMessage = JsonDocument.Parse("""{ "error": { "code": -32602, "message": "invalid params" } }""");

        McpJson.GetJsonRpcErrorMessage(missingError.RootElement)
            .Should().Be("The MCP server returned a JSON-RPC error.");
        McpJson.GetJsonRpcErrorMessage(messageOnly.RootElement)
            .Should().Be("bad request");
        McpJson.GetJsonRpcErrorMessage(codeOnly.RootElement)
            .Should().Be("The MCP server returned JSON-RPC error -32001.");
        McpJson.GetJsonRpcErrorMessage(codeAndMessage.RootElement)
            .Should().Be("The MCP server returned JSON-RPC error -32602: invalid params");
    }
}
