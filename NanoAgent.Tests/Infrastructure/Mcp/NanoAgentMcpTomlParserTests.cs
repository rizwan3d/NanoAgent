using FluentAssertions;
using NanoAgent.Infrastructure.Mcp;

namespace NanoAgent.Tests.Infrastructure.Mcp;

public sealed class NanoAgentMcpTomlParserTests : IDisposable
{
    private readonly string _tempDirectory;

    public NanoAgentMcpTomlParserTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "nanoagent-mcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Parse_Should_ReadStdioServer()
    {
        string configPath = WriteConfig(
            """
            [mcp_servers.context7]
            command = "npx"
            args = ["-y", "@upstash/context7-mcp"]
            env_vars = ["LOCAL_TOKEN", { name = "REMOTE_TOKEN", source = "remote" }]
            cwd = "."
            startup_timeout_sec = 20
            tool_timeout_sec = 45
            enabled_tools = ["resolve-library-id", "get-library-docs"]
            disabled_tools = ["get-library-docs"]
            default_tools_approval_mode = "approve"

            [mcp_servers.context7.env]
            MY_ENV_VAR = "MY_ENV_VALUE"

            [mcp_servers.context7.tools.resolve-library-id]
            approval_mode = "prompt"
            """);

        McpServerConfiguration server = NanoAgentMcpTomlParser.Parse(configPath)
            .Should()
            .ContainSingle()
            .Subject;

        server.Name.Should().Be("context7");
        server.Command.Should().Be("npx");
        server.Args.Should().Equal("-y", "@upstash/context7-mcp");
        server.EnvVars.Should().Equal("LOCAL_TOKEN", "REMOTE_TOKEN");
        server.Env.Should().Contain("MY_ENV_VAR", "MY_ENV_VALUE");
        server.Cwd.Should().Be(".");
        server.StartupTimeoutSeconds.Should().Be(20);
        server.ToolTimeoutSeconds.Should().Be(45);
        server.EnabledTools.Should().Equal("resolve-library-id", "get-library-docs");
        server.DisabledTools.Should().Equal("get-library-docs");
        server.DefaultToolsApprovalMode.Should().Be("approve");
        server.ToolApprovalModes.Should().Contain("resolve-library-id", "prompt");
        server.ShouldIncludeTool("resolve-library-id").Should().BeTrue();
        server.ShouldIncludeTool("get-library-docs").Should().BeFalse();
    }

    [Fact]
    public void Parse_Should_ReadStreamableHttpServer()
    {
        string configPath = WriteConfig(
            """
            [mcp_servers.figma]
            url = "https://mcp.figma.com/mcp"
            bearer_token_env_var = "FIGMA_TOKEN"
            http_headers = { "X-Figma-Region" = "us-east-1" }
            env_http_headers = { "X-Token" = "LOCAL_TOKEN" }
            enabled = true
            required = true
            """);

        McpServerConfiguration server = NanoAgentMcpTomlParser.Parse(configPath)
            .Should()
            .ContainSingle()
            .Subject;

        server.Name.Should().Be("figma");
        server.Url.Should().Be("https://mcp.figma.com/mcp");
        server.BearerTokenEnvVar.Should().Be("FIGMA_TOKEN");
        server.HttpHeaders.Should().Contain("X-Figma-Region", "us-east-1");
        server.EnvHttpHeaders.Should().Contain("X-Token", "LOCAL_TOKEN");
        server.Enabled.Should().BeTrue();
        server.Required.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string WriteConfig(string content)
    {
        string path = Path.Combine(_tempDirectory, "config.toml");
        File.WriteAllText(path, content);
        return path;
    }
}
