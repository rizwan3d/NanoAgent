using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Storage;
using FluentAssertions;

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
