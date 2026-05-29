using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.CodeIntelligence;

namespace NanoAgent.Tests.Infrastructure.CodeIntelligence;

public sealed class LanguageServerRegistryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _workspaceRoot;
    private readonly string _userProfilePath;

    public LanguageServerRegistryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"NanoAgent-LspRegistry-{Guid.NewGuid():N}");
        _workspaceRoot = Path.Combine(_tempRoot, "workspace");
        _userProfilePath = Path.Combine(_tempRoot, "appdata", "NanoAgent", "agent-profile.json");
        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public async Task ResolveAsync_Should_OrderDetectedServersByPriority()
    {
        WriteFile(
            Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json"),
            """
            {
              "languageServers": {
                "python-low": {
                  "language": "Python",
                  "name": "Low",
                  "command": ".nanoagent/bin/low.cmd",
                  "languageId": "python",
                  "fileExtensions": [".pyi"],
                  "priority": 100
                },
                "python-high": {
                  "language": "Python",
                  "name": "High",
                  "command": ".nanoagent/bin/high.cmd",
                  "languageId": "python",
                  "fileExtensions": [".pyi"],
                  "priority": 300
                }
              }
            }
            """);
        WriteFile(Path.Combine(_workspaceRoot, ".nanoagent", "bin", "low.cmd"), "@echo off");
        WriteFile(Path.Combine(_workspaceRoot, ".nanoagent", "bin", "high.cmd"), "@echo off");

        LanguageServerRegistry sut = CreateSut();

        LanguageServerResolution result = await sut.ResolveAsync(
            _workspaceRoot,
            Path.Combine(_workspaceRoot, "app.pyi"),
            refresh: false,
            CancellationToken.None);

        result.Candidates.Select(static candidate => candidate.Descriptor.Key)
            .Should()
            .Equal("python-high", "python-low");
    }

    [Fact]
    public async Task ResolveAsync_Should_DetectWorkspaceNodeModulesServer()
    {
        WriteFile(
            Path.Combine(_workspaceRoot, "node_modules", ".bin", "typescript-language-server.cmd"),
            "@echo off");

        LanguageServerRegistry sut = CreateSut();

        LanguageServerResolution result = await sut.ResolveAsync(
            _workspaceRoot,
            Path.Combine(_workspaceRoot, "index.ts"),
            refresh: false,
            CancellationToken.None);

        result.Candidates.Should().Contain(candidate =>
            candidate.Descriptor.Key == "ts-typescript-language-server" &&
            candidate.Probe.State == LanguageServerHealthState.Detected);
    }

    [Fact]
    public async Task GetStatusAsync_Should_ReportMissingServersAndInstallHints()
    {
        WriteFile(
            Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json"),
            """
            {
              "languageServers": {
                "custom-ruby": {
                  "language": "Ruby",
                  "name": "Custom Ruby",
                  "command": ".nanoagent/bin/ruby-lsp.cmd",
                  "languageId": "ruby",
                  "fileExtensions": [".rb"],
                  "priority": 200,
                  "installHint": "gem install ruby-lsp"
                }
              }
            }
            """);

        LanguageServerRegistry sut = CreateSut();

        IReadOnlyList<LanguageServerStatusEntry> status = await sut.GetStatusAsync(
            _workspaceRoot,
            refresh: false,
            CancellationToken.None);

        LanguageServerStatusEntry ruby = status.Single(static item => item.Language == "Ruby");
        ruby.Candidates.Should().Contain(candidate =>
            candidate.Key == "custom-ruby" &&
            candidate.DetectionStatus == "missing" &&
            candidate.InstallHint == "gem install ruby-lsp");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private LanguageServerRegistry CreateSut()
    {
        return new LanguageServerRegistry(
            new StubUserDataPathProvider(_userProfilePath),
            new StubWorkspaceRootProvider(_workspaceRoot));
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

        public string GetSessionsDirectoryPath()
        {
            return Path.Combine(Path.GetDirectoryName(_profilePath)!, "sessions");
        }
    }
}
