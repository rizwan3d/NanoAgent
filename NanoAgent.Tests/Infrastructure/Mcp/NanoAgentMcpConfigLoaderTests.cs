using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Mcp;

namespace NanoAgent.Tests.Infrastructure.Mcp;

public sealed class NanoAgentMcpConfigLoaderTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _userConfigPath;
    private readonly string _workspaceRoot;

    public NanoAgentMcpConfigLoaderTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-McpLoader-{Guid.NewGuid():N}");
        _workspaceRoot = Path.Combine(_tempRoot, "workspace");
        _userConfigPath = Path.Combine(_tempRoot, "appdata", "NanoAgent", "agent-profile.json");

        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public void Load_Should_ReadNanoAgentUserAndWorkspaceConfigs()
    {
        WriteConfig(
            _userConfigPath,
            """
            {
              "mcpServers": {
                "context7": {
                  "command": "npx",
                  "args": ["global"],
                  "cwd": ".mcp"
                }
              }
            }
            """);
        WriteConfig(
            Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json"),
            """
            {
              "mcpServers": {
                "context7": {
                  "args": ["workspace"],
                  "startupTimeoutSeconds": 20
                },
                "workspace": {
                  "command": "dotnet",
                  "args": ["tool"]
                }
              }
            }
            """);
        NanoAgentMcpConfigLoader sut = CreateLoader();

        IReadOnlyList<McpServerConfiguration> servers = sut.Load();

        servers.Select(static server => server.Name).Should().Equal("context7", "workspace");
        McpServerConfiguration context7 = servers.Should()
            .ContainSingle(static server => server.Name == "context7")
            .Subject;
        context7.Command.Should().Be("npx");
        context7.Args.Should().Equal("workspace");
        context7.StartupTimeoutSeconds.Should().Be(20);
        context7.Cwd.Should().Be(Path.Combine(_workspaceRoot, ".mcp"));
    }

    [Fact]
    public void Load_Should_IgnoreLegacyTomlConfigs()
    {
        WriteConfig(
            Path.Combine(_workspaceRoot, ".nanoagent", "config.toml"),
            """
            [mcp_servers.legacy]
            command = "npx"
            """);
        WriteConfig(
            Path.Combine(_tempRoot, "appdata", "NanoAgent", "mcp.toml"),
            """
            [mcp_servers.user_legacy]
            command = "npx"
            """);
        NanoAgentMcpConfigLoader sut = CreateLoader();

        IReadOnlyList<McpServerConfiguration> servers = sut.Load();

        servers.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private NanoAgentMcpConfigLoader CreateLoader()
    {
        return new NanoAgentMcpConfigLoader(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubUserDataPathProvider(_userConfigPath));
    }

    private static void WriteConfig(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class StubWorkspaceRootProvider : IWorkspaceRootProvider
    {
        private readonly string _workspaceRoot;

        public StubWorkspaceRootProvider(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        public string GetWorkspaceRoot()
        {
            return _workspaceRoot;
        }
    }

    private sealed class StubUserDataPathProvider : IUserDataPathProvider
    {
        private readonly string _mcpConfigurationFilePath;

        public StubUserDataPathProvider(string mcpConfigurationFilePath)
        {
            _mcpConfigurationFilePath = mcpConfigurationFilePath;
        }

        public string GetConfigurationFilePath()
        {
            return _mcpConfigurationFilePath;
        }

        public string GetMcpConfigurationFilePath()
        {
            return GetConfigurationFilePath();
        }

        public string GetLogsDirectoryPath()
        {
            return Path.Combine(Path.GetDirectoryName(_mcpConfigurationFilePath)!, "logs");
        }

        public string GetSectionsDirectoryPath()
        {
            return Path.Combine(Path.GetDirectoryName(_mcpConfigurationFilePath)!, "sections");
        }
    }
}
