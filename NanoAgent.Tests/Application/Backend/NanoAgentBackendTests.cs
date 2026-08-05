using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Reflection;

namespace NanoAgent.Tests.Application.Backend;

public sealed class NanoAgentBackendTests
{
    [Fact]
    public void Ctor_WithWorkspaceRoot_Should_OverrideWorkspaceRootProvider()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        NanoAgentBackend backend = new([], [], autoApproveAllTools: false, workspace.Path);

        Action<IServiceCollection>? configureServices = typeof(NanoAgentBackend)
            .GetField("_configureServices", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(backend) as Action<IServiceCollection>;

        configureServices.Should().NotBeNull();

        ServiceCollection services = [];
        services.AddSingleton<IWorkspaceRootProvider>(new StubWorkspaceRootProvider("fallback-root"));
        configureServices!(services);

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IWorkspaceRootProvider>()
            .GetWorkspaceRoot()
            .Should()
            .Be(workspace.Path);
    }

    [Fact]
    public void GetFileEditSummary_Should_OnlyIncludeEditsCreatedAfterInitializationBaseline()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        NanoAgentBackend backend = new([], [], autoApproveAllTools: false, workspace.Path);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model-a",
            ["model-a"],
            workspacePath: workspace.Path);

        session.RecordEditContext(new SessionEditContext(
            DateTimeOffset.UtcNow,
            "file_write created (docs/old.md)",
            ["docs/old.md"],
            3,
            0));

        int startingEditIndex = session.GetRecordedEditCount();

        session.RecordEditContext(new SessionEditContext(
            DateTimeOffset.UtcNow,
            "file_write created (src/new.cs)",
            ["src/new.cs"],
            5,
            1));

        typeof(NanoAgentBackend)
            .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(backend, session);
        typeof(NanoAgentBackend)
            .GetField("_editListStartIndex", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(backend, startingEditIndex);

        IReadOnlyList<FileEditSummary> summary = backend.GetFileEditSummary();

        summary.Should().ContainSingle();
        summary[0].DisplayPath.Should().Be("src/new.cs");
        summary[0].AddedLineCount.Should().Be(5);
        summary[0].RemovedLineCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_Should_StopSession_WhenAutoCommitThrows()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        NanoAgentBackend backend = new([], [], autoApproveAllTools: false, workspace.Path);
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model-a",
            ["model-a"],
            workspacePath: workspace.Path);
        ThrowingAutoCommitService autoCommitService = new();
        RecordingSessionAppService sessionAppService = new();

        typeof(NanoAgentBackend)
            .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(backend, session);
        typeof(NanoAgentBackend)
            .GetField("_autoCommitService", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(backend, autoCommitService);
        typeof(NanoAgentBackend)
            .GetField("_sessionAppService", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(backend, sessionAppService);

        await backend.DisposeAsync();

        autoCommitService.CallCount.Should().Be(1);
        sessionAppService.StopCallCount.Should().Be(1);
        sessionAppService.StoppedSession.Should().BeSameAs(session);
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

    private sealed class ThrowingAutoCommitService : IAutoCommitService
    {
        public int CallCount { get; private set; }

        public Task TryAutoCommitAsync(
            ReplSessionContext session,
            IReadOnlyList<SessionEditContext> newEdits,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Auto-commit failed.");
        }
    }

    private sealed class RecordingSessionAppService : ISessionAppService
    {
        public int StopCallCount { get; private set; }

        public ReplSessionContext? StoppedSession { get; private set; }

        public Task<ReplSessionContext> CreateAsync(
            CreateSessionRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ReplSessionContext> CreateNewSectionInSessionAsync(
            ReplSessionContext currentSession,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public void EnsureTitleGenerationStarted(
            ReplSessionContext session,
            string firstUserPrompt)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ReplSessionContext> ResumeAsync(
            ResumeSessionRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SaveIfDirtyAsync(
            ReplSessionContext session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task StopAsync(
            ReplSessionContext session,
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            StoppedSession = session;
            return Task.CompletedTask;
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempWorkspace Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "nanoagent-backend-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempWorkspace(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
