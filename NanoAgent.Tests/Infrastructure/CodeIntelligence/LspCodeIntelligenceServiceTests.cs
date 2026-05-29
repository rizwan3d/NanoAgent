using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.CodeIntelligence;

namespace NanoAgent.Tests.Infrastructure.CodeIntelligence;

public sealed class LspCodeIntelligenceServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _workspaceRoot;

    public LspCodeIntelligenceServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"NanoAgent-LspService-{Guid.NewGuid():N}");
        _workspaceRoot = Path.Combine(_tempRoot, "workspace");
        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public async Task QueryAsync_Should_FallBackToNextServer_When_FirstServerFails()
    {
        string filePath = WriteWorkspaceFile("Program.cs", "class Program {}");
        FakeLanguageServerRegistry registry = new(new LanguageServerResolution(
            ".cs",
            "csharp",
            [
                CreateResolvedServer("primary", "Primary", "primary.cmd", LanguageServerHealthState.Detected),
                CreateResolvedServer("secondary", "Secondary", "secondary.cmd", LanguageServerHealthState.Detected)
            ]));

        LspCodeIntelligenceService sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            registry,
            (server, request, workspaceRoot, rootUri, fileUri, relativePath, sourceText, warnings, cancellationToken) =>
            {
                if (server.Key == "primary")
                {
                    throw new InvalidOperationException("failed to start");
                }

                return Task.FromResult(new CodeIntelligenceResult(
                    request.Action,
                    relativePath,
                    server.LanguageId,
                    server.Name,
                    [new CodeIntelligenceItem("Definition", "Program", null, relativePath, 1, 1, 1, 7, null)],
                    HoverText: null,
                    Warnings: warnings.ToArray()));
            });

        CodeIntelligenceResult result = await sut.QueryAsync(
            new CodeIntelligenceRequest("definition", "Program.cs", 1, 1, false, 5),
            CancellationToken.None);

        result.ServerName.Should().Be("Secondary");
        result.Warnings.Should().ContainSingle();
        result.Warnings[0].Should().Contain("Primary");
        registry.RecordedStates.Should().ContainKey("secondary");
        registry.RecordedStates["secondary"].Should().Be(LanguageServerHealthState.Healthy);
    }

    [Fact]
    public async Task QueryAsync_Should_ReportTimeoutAttempts()
    {
        WriteWorkspaceFile("Program.cs", "class Program {}");
        FakeLanguageServerRegistry registry = new(new LanguageServerResolution(
            ".cs",
            "csharp",
            [CreateResolvedServer("primary", "Primary", "primary.cmd", LanguageServerHealthState.Detected)]));

        LspCodeIntelligenceService sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            registry,
            static (_, _, _, _, _, _, _, _, _) => throw new OperationCanceledException());

        Func<Task> action = async () => await sut.QueryAsync(
            new CodeIntelligenceRequest("definition", "Program.cs", 1, 1, false, 1),
            CancellationToken.None);

        var assertion = await action.Should().ThrowAsync<CodeIntelligenceUnavailableException>();
        assertion.Which.Attempts.Should().ContainSingle();
        assertion.Which.Attempts[0].Should().Contain("timed out");
    }

    [Fact]
    public async Task QueryAsync_Should_ReportUnsupportedMethodAttempts()
    {
        WriteWorkspaceFile("Program.cs", "class Program {}");
        FakeLanguageServerRegistry registry = new(new LanguageServerResolution(
            ".cs",
            "csharp",
            [CreateResolvedServer("primary", "Primary", "primary.cmd", LanguageServerHealthState.Detected)]));

        LspCodeIntelligenceService sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            registry,
            static (_, _, _, _, _, _, _, _, _) => throw new InvalidOperationException("does not support code_intelligence action 'rename_symbol'."));

        Func<Task> action = async () => await sut.QueryAsync(
            new CodeIntelligenceRequest("rename_symbol", "Program.cs", 1, 1, false, 5, NewName: "Renamed"),
            CancellationToken.None);

        var assertion = await action.Should().ThrowAsync<CodeIntelligenceUnavailableException>();
        assertion.Which.Attempts[0].Should().Contain("does not support");
    }

    [Fact]
    public async Task QueryAsync_Should_ReturnServerStatusResult()
    {
        FakeLanguageServerRegistry registry = new(new[]
        {
            new LanguageServerStatusEntry(
                "Python",
                "python",
                [".py"],
                [
                    new LanguageServerCandidateStatus(
                        "python-pyright",
                        "Pyright",
                        "pyright-langserver",
                        ["--stdio"],
                        200,
                        "detected",
                        "built-in",
                        "C:\\tools\\pyright-langserver.cmd",
                        "npm install -g pyright",
                        "python",
                        null)
                ],
                "Pyright")
        });

        LspCodeIntelligenceService sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            registry,
            static (_, _, _, _, _, _, _, _, _) => throw new NotSupportedException());

        CodeIntelligenceResult result = await sut.QueryAsync(
            new CodeIntelligenceRequest("servers_status", ".", null, null, false, 5),
            CancellationToken.None);

        result.Action.Should().Be("servers_status");
        result.Servers.Should().ContainSingle();
        result.Servers![0].SelectedServerName.Should().Be("Pyright");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private string WriteWorkspaceFile(string relativePath, string content)
    {
        string path = Path.Combine(_workspaceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static ResolvedLanguageServer CreateResolvedServer(
        string key,
        string name,
        string command,
        LanguageServerHealthState state)
    {
        LanguageServerDescriptor descriptor = new(
            key,
            "C#",
            name,
            command,
            [],
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".cs"] = "csharp"
            },
            [".cs"],
            100,
            null,
            null,
            true,
            "test");

        return new ResolvedLanguageServer(
            descriptor,
            new LanguageServerProbeResult(state, command, null));
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

    private sealed class FakeLanguageServerRegistry : ILanguageServerRegistry
    {
        private readonly LanguageServerResolution? _resolution;
        private readonly IReadOnlyList<LanguageServerStatusEntry> _status;

        public FakeLanguageServerRegistry(LanguageServerResolution resolution)
        {
            _resolution = resolution;
            _status = [];
        }

        public FakeLanguageServerRegistry(IReadOnlyList<LanguageServerStatusEntry> status)
        {
            _status = status;
        }

        public Dictionary<string, LanguageServerHealthState> RecordedStates { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<LanguageServerStatusEntry>> GetStatusAsync(string workspaceRoot, bool refresh, CancellationToken cancellationToken)
        {
            return Task.FromResult(_status);
        }

        public void RecordServerHealth(string workspaceRoot, string serverKey, LanguageServerHealthState state, string? message = null)
        {
            RecordedStates[serverKey] = state;
        }

        public Task<LanguageServerResolution> ResolveAsync(string workspaceRoot, string fullPath, bool refresh, CancellationToken cancellationToken)
        {
            return Task.FromResult(_resolution!);
        }
    }
}
