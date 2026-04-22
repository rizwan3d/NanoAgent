using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Infrastructure.Tools;
using NanoAgent.Tests.Infrastructure.Secrets.TestDoubles;
using FluentAssertions;

namespace NanoAgent.Tests.Infrastructure.Tools;

public sealed class ShellCommandServiceTests : IDisposable
{
    private readonly string _workspaceRoot;

    public ShellCommandServiceTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Shell-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));
    }

    [Fact]
    public async Task ExecuteAsync_Should_PreserveCompoundCommands_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot));

        await sut.ExecuteAsync(
            new ShellCommandExecutionRequest("node -v && npm -v", "src"),
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
        processRunner.Requests[0].FileName.Should().Be("/bin/bash");
        processRunner.Requests[0].Arguments.Should().Equal("-lc", "node -v && npm -v");
        processRunner.Requests[0].WorkingDirectory.Should().Be(Path.Combine(_workspaceRoot, "src"));
    }

    [Fact]
    public async Task ExecuteAsync_Should_TranslateCompoundCommands_ForWindowsPowerShell()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot));

        await sut.ExecuteAsync(
            new ShellCommandExecutionRequest("mkdir todo && cd todo && npm i", null),
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.Requests[0];
        request.FileName.Should().Be("powershell");
        request.Arguments.Should().Contain("-Command");
        request.Arguments[^1].Should().Contain("Invoke-NanoSegment");
        request.Arguments[^1].Should().Contain("FromBase64String");
        request.Arguments[^1].Should().Contain("$__nano_exit = Invoke-NanoSegment");
        request.Arguments[^1].Should().NotContain("&&");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private sealed class StubWorkspaceRootProvider : NanoAgent.Application.Abstractions.IWorkspaceRootProvider
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
}
