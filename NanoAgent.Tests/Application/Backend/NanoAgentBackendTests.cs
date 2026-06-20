using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Backend;
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
