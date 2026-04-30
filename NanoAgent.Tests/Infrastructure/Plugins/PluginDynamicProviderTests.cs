using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Tools.Services;
using NanoAgent.Infrastructure.Plugins;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace NanoAgent.Tests.Infrastructure.Plugins;

public sealed class PluginDynamicProviderTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _userProfilePath;
    private readonly string _workspaceRoot;

    public PluginDynamicProviderTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Plugins-{Guid.NewGuid():N}");
        _workspaceRoot = Path.Combine(_tempRoot, "workspace");
        _userProfilePath = Path.Combine(_tempRoot, "appdata", "NanoAgent", "agent-profile.json");
        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public void GetTools_Should_LoadConfiguredPlugins()
    {
        WriteFile(
            Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json"),
            """
            {
              "plugins": {
                "demo": {
                  "approvalMode": "auto",
                  "settings": {
                    "label": "Workspace demo"
                  },
                  "tools": {
                    "ping": {
                      "approvalMode": "prompt"
                    }
                  }
                }
              }
            }
            """);

        PluginDynamicProvider provider = CreateProvider([new DemoPluginToolFactory()]);
        ToolRegistry registry = new(
            [],
            new ToolPermissionParser(),
            [provider]);

        registry.TryResolve("plugin__demo__ping", out ToolRegistration? registration)
            .Should()
            .BeTrue();
        registration!.Tool.Description.Should().Be("Workspace demo");
        registration.PermissionPolicy.ApprovalMode.Should().Be(ToolApprovalMode.RequireApproval);
        registration.PermissionPolicy.ToolTags.Should().Contain("plugin:demo");
        provider.GetStatuses().Should().ContainSingle(status =>
            status.Name == "demo" &&
            status.Kind == "plugin" &&
            status.IsAvailable &&
            status.ToolCount == 1);
    }

    [Fact]
    public void GetTools_Should_ReportConfiguredUnknownPlugins()
    {
        WriteFile(
            Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json"),
            """
            {
              "plugins": {
                "missing": {
                  "enabled": true
                }
              }
            }
            """);

        PluginDynamicProvider provider = CreateProvider([]);

        provider.GetTools().Should().BeEmpty();
        provider.GetStatuses().Should().ContainSingle(status =>
            status.Name == "missing" &&
            status.Enabled &&
            !status.IsAvailable &&
            status.Details == "plugin is not installed");
    }

    [Fact]
    public void GetTools_Should_AllowInstalledPluginsByDefault()
    {
        PluginDynamicProvider provider = CreateProvider([new DemoPluginToolFactory()]);

        provider.GetTools().Should().ContainSingle(tool => tool.Name == "plugin__demo__ping");
        provider.GetStatuses().Should().ContainSingle(status =>
            status.Name == "demo" &&
            status.Enabled &&
            status.IsAvailable);
    }

    [Fact]
    public void GetTools_Should_HonorDisabledConfiguredPlugin()
    {
        WriteFile(
            Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json"),
            """
            {
              "plugins": {
                "demo": {
                  "enabled": false
                }
              }
            }
            """);

        PluginDynamicProvider provider = CreateProvider([new DemoPluginToolFactory()]);

        provider.GetTools().Should().BeEmpty();
        provider.GetStatuses().Should().ContainSingle(status =>
            status.Name == "demo" &&
            !status.Enabled &&
            !status.IsAvailable &&
            status.Details == "disabled");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private PluginDynamicProvider CreateProvider(IReadOnlyList<IPluginToolFactory> factories)
    {
        return new PluginDynamicProvider(
            factories,
            new StubUserDataPathProvider(_userProfilePath),
            new StubWorkspaceRootProvider(_workspaceRoot),
            NullLogger<PluginDynamicProvider>.Instance);
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class DemoPluginToolFactory : IPluginToolFactory
    {
        public string PluginName => "demo";

        public IReadOnlyList<ITool> CreateTools(PluginConfiguration configuration)
        {
            return [new DemoPluginTool(configuration)];
        }
    }

    private sealed class DemoPluginTool : ITool
    {
        private readonly PluginConfiguration _configuration;

        public DemoPluginTool(PluginConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string Description => _configuration.GetSetting("label") ?? "Demo plugin tool.";

        public string Name => PluginToolName.Create("demo", "ping");

        public string PermissionRequirements => PluginJson.CreatePermissionRequirements(
            "demo",
            "ping",
            _configuration.GetApprovalMode("ping"));

        public string Schema => """{ "type": "object", "properties": {}, "additionalProperties": false }""";

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
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
        private readonly string _profilePath;

        public StubUserDataPathProvider(string profilePath)
        {
            _profilePath = profilePath;
        }

        public string GetConfigurationFilePath()
        {
            return _profilePath;
        }

        public string GetMcpConfigurationFilePath()
        {
            return _profilePath;
        }

        public string GetLogsDirectoryPath()
        {
            return Path.Combine(Path.GetDirectoryName(_profilePath)!, "logs");
        }

        public string GetSectionsDirectoryPath()
        {
            return Path.Combine(Path.GetDirectoryName(_profilePath)!, "sections");
        }
    }
}
