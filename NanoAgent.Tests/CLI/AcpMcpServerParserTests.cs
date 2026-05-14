using System.Text.Json;
using FluentAssertions;
using NanoAgent.Application.Backend;
using NanoAgent.CLI;

namespace NanoAgent.Tests.CLI;

public sealed class AcpMcpServerParserTests
{
    [Fact]
    public void Parse_Should_Return_Empty_When_ParamsNotObject()
    {
        using JsonDocument doc = JsonDocument.Parse("[]");
        var result = AcpMcpServerParser.Parse(doc.RootElement, "test");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Should_Return_Empty_When_NoMcpServers()
    {
        using JsonDocument doc = JsonDocument.Parse("""{ "key": "value" }""");
        var result = AcpMcpServerParser.Parse(doc.RootElement, "test");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Should_Parse_ServerObject_From_ObjectFormat()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """{ "mcpServers": { "name": "my-server", "command": "node", "args": ["server.js"] } }""");

        var result = AcpMcpServerParser.Parse(doc.RootElement, "config");

        result.Should().ContainSingle();
        result[0].Name.Should().Be("my-server");
        result[0].Command.Should().Be("node");
        result[0].Args.Should().BeEquivalentTo(["server.js"]);
    }

    [Fact]
    public void Parse_Should_Parse_MultipleServers_From_DictionaryFormat()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """{ "mcpServers": { "server1": { "command": "node", "args": ["s1.js"] }, "server2": { "command": "python", "args": ["s2.py"] } } }""");

        var result = AcpMcpServerParser.Parse(doc.RootElement, "config");

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("server1");
        result[0].Command.Should().Be("node");
        result[1].Name.Should().Be("server2");
        result[1].Command.Should().Be("python");
    }

    [Fact]
    public void Parse_Should_Parse_Servers_From_NestedConfig()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """{ "config": { "mcpServers": [{ "name": "nested-server", "command": "dotnet", "args": ["run"] }] } }""");

        var result = AcpMcpServerParser.Parse(doc.RootElement, "config");

        result.Should().ContainSingle();
        result[0].Name.Should().Be("nested-server");
    }

    [Fact]
    public void Parse_Should_Parse_Servers_From_ArrayFormat()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """{ "mcpServers": [{ "name": "s1", "url": "http://localhost:8080" }] }""");

        var result = AcpMcpServerParser.Parse(doc.RootElement, "config");

        result.Should().ContainSingle();
        result[0].Name.Should().Be("s1");
        result[0].Url.Should().Be("http://localhost:8080");
    }

    [Fact]
    public void Parse_Should_Apply_Enabled_Field()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """{ "mcpServers": { "s1": { "command": "node", "enabled": false } } }""");

        var result = AcpMcpServerParser.Parse(doc.RootElement, "config");

        result.Should().ContainSingle();
        result[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void Parse_Should_Apply_Env_Dictionary()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """{ "mcpServers": { "s1": { "command": "node", "env": { "KEY": "value" } } } }""");

        var result = AcpMcpServerParser.Parse(doc.RootElement, "config");

        result.Should().ContainSingle();
        result[0].Env.Should().ContainKey("KEY").WhoseValue.Should().Be("value");
    }

    [Fact]
    public void Parse_Should_Skip_Server_Without_Name()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """{ "mcpServers": [{ "command": "node" }] }""");

        var result = AcpMcpServerParser.Parse(doc.RootElement, "config");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Should_Apply_HttpHeaders()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """{ "mcpServers": { "s1": { "command": "node", "httpHeaders": { "Authorization": "Bearer token123" } } } }""");

        var result = AcpMcpServerParser.Parse(doc.RootElement, "config");

        result.Should().ContainSingle();
        result[0].HttpHeaders.Should().ContainKey("Authorization").WhoseValue.Should().Be("Bearer token123");
    }
}
