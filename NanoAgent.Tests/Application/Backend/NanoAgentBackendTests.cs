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
