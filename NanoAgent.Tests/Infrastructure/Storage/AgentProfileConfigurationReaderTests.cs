using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Storage;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class AgentProfileConfigurationReaderTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _userProfilePath;
    private readonly string _workspaceRoot;

    public AgentProfileConfigurationReaderTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-AgentProfile-{Guid.NewGuid():N}");
        _workspaceRoot = Path.Combine(_tempRoot, "workspace");
        _userProfilePath = Path.Combine(_tempRoot, "appdata", "NanoAgent", "agent-profile.json");
        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public void LoadMemorySettings_Should_MergeUserAndWorkspaceAgentProfiles()
    {
        WriteFile(
            _userProfilePath,
            """
            {
              "memory": {
                "requireApprovalForWrites": true,
                "allowAutoFailureObservation": true,
                "allowAutoManualLessons": false,
                "redactSecrets": true,
                "maxEntries": 500,
                "maxPromptChars": 12000,
                "disabled": false
              },
              "toolAudit": {
                "enabled": false,
                "maxArgumentsChars": 4000,
                "maxResultChars": 5000,
                "redactSecrets": true
              }
            }
            """);
        WriteFile(
            Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json"),
            """
            {
              "memory": {
                "maxEntries": 250,
                "maxPromptChars": 6000
              },
              "toolAuditLog": {
                "enabled": true,
                "maxResultChars": 6000
              }
            }
            """);

        var settings = AgentProfileConfigurationReader.LoadMemorySettings(
            new StubUserDataPathProvider(_userProfilePath),
            new StubWorkspaceRootProvider(_workspaceRoot));

        settings.RequireApprovalForWrites.Should().BeTrue();
        settings.AllowAutoFailureObservation.Should().BeTrue();
        settings.AllowAutoManualLessons.Should().BeFalse();
        settings.RedactSecrets.Should().BeTrue();
        settings.MaxEntries.Should().Be(250);
        settings.MaxPromptChars.Should().Be(6000);
        settings.Disabled.Should().BeFalse();

        var auditSettings = AgentProfileConfigurationReader.LoadToolAuditSettings(
            new StubUserDataPathProvider(_userProfilePath),
            new StubWorkspaceRootProvider(_workspaceRoot));

        auditSettings.Enabled.Should().BeTrue();
        auditSettings.MaxArgumentsChars.Should().Be(4000);
        auditSettings.MaxResultChars.Should().Be(6000);
        auditSettings.RedactSecrets.Should().BeTrue();
    }

    [Fact]
    public void LoadToolAuditSettings_Should_DefaultToDisabled()
    {
        var settings = AgentProfileConfigurationReader.LoadToolAuditSettings(
            new StubUserDataPathProvider(_userProfilePath),
            new StubWorkspaceRootProvider(_workspaceRoot));

        settings.Enabled.Should().BeFalse();
        settings.MaxArgumentsChars.Should().Be(12_000);
        settings.MaxResultChars.Should().Be(12_000);
        settings.RedactSecrets.Should().BeTrue();
    }

    [Fact]
    public void LoadCustomTools_Should_LoadWorkspaceToolsAndResolveRelativePaths()
    {
        WriteFile(
            Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json"),
            """
            {
              "customTools": {
                "word_count": {
                  "description": "Count words.",
                  "command": ".nanoagent/tools/word_count.py",
                  "args": ["--json"],
                  "cwd": ".",
                  "approvalMode": "auto",
                  "timeoutSeconds": 15,
                  "maxOutputChars": 3000,
                  "schema": {
                    "type": "object",
                    "properties": {
                      "text": { "type": "string" }
                    },
                    "required": ["text"],
                    "additionalProperties": false
                  }
                }
              }
            }
            """);

        var tools = AgentProfileConfigurationReader.LoadCustomTools(
            new StubUserDataPathProvider(_userProfilePath),
            new StubWorkspaceRootProvider(_workspaceRoot));

        tools.Should().ContainSingle();
        tools[0].Name.Should().Be("word_count");
        tools[0].Description.Should().Be("Count words.");
        tools[0].Command.Should().Be(Path.GetFullPath(Path.Combine(_workspaceRoot, ".nanoagent/tools/word_count.py")));
        tools[0].Args.Should().Equal("--json");
        tools[0].Cwd.Should().Be(Path.GetFullPath(_workspaceRoot));
        tools[0].ApprovalMode.Should().Be("auto");
        tools[0].TimeoutSeconds.Should().Be(15);
        tools[0].MaxOutputChars.Should().Be(3000);
        tools[0].Schema.Should().NotBeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static void WriteFile(string path, string content)
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
