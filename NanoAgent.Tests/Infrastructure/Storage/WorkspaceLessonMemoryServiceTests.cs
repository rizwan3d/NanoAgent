using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Infrastructure.Storage;
using FluentAssertions;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class WorkspaceLessonMemoryServiceTests
{
    [Fact]
    public async Task ObserveToolResultAsync_Should_RecordAndFixShellBuildFailures()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        WorkspaceLessonMemoryService sut = CreateService(workspace.Path);

        await sut.ObserveToolResultAsync(
            CreateShellInvocation(new ShellCommandExecutionResult(
                "dotnet build",
                ".",
                1,
                "",
                "Program.cs(10,5): error CS0246: The type or namespace name 'MissingType' could not be found.")),
            CancellationToken.None);

        IReadOnlyList<LessonMemoryEntry> failures = await sut.ListAsync(
            limit: 10,
            includeFixed: true,
            CancellationToken.None);

        failures.Should().ContainSingle();
        failures[0].Kind.Should().Be("failure");
        failures[0].IsFixed.Should().BeFalse();
        failures[0].FailureSignature.Should().Be("CS0246");
        failures[0].Tags.Should().Contain(["auto", "failure", "build"]);

        await sut.ObserveToolResultAsync(
            CreateShellInvocation(new ShellCommandExecutionResult(
                "dotnet build",
                ".",
                0,
                "Build succeeded.",
                "")),
            CancellationToken.None);

        IReadOnlyList<LessonMemoryEntry> fixedFailures = await sut.ListAsync(
            limit: 10,
            includeFixed: true,
            CancellationToken.None);

        fixedFailures.Should().ContainSingle();
        fixedFailures[0].IsFixed.Should().BeTrue();
        fixedFailures[0].FixedAtUtc.Should().NotBeNull();
        fixedFailures[0].FixSummary.Should().Contain("exited 0");
    }

    [Fact]
    public async Task CreatePromptAsync_Should_ReturnRelevantLessons()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        WorkspaceLessonMemoryService sut = CreateService(workspace.Path);
        await sut.SaveAsync(
            new LessonMemorySaveRequest(
                "CS0246 during build",
                "A service registration was missing",
                "Check DI registration before editing unrelated files.",
                ["build", "csharp", "CS0246"]),
            CancellationToken.None);

        string? prompt = await sut.CreatePromptAsync(
            "Fix the build CS0246",
            CancellationToken.None);

        prompt.Should().NotBeNull();
        prompt.Should().Contain("Relevant lesson memory");
        prompt.Should().Contain("Check DI registration");
        prompt.Should().Contain(".nanoagent/memory/lessons.jsonl");
    }

    private static ToolInvocationResult CreateShellInvocation(ShellCommandExecutionResult result)
    {
        return new ToolInvocationResult(
            "call_shell",
            AgentToolNames.ShellCommand,
            ToolResultFactory.Success(
                "shell",
                result,
                ToolJsonContext.Default.ShellCommandExecutionResult));
    }

    private static WorkspaceLessonMemoryService CreateService(string workspacePath)
    {
        return new WorkspaceLessonMemoryService(
            new FixedWorkspaceRootProvider(workspacePath),
            TimeProvider.System);
    }

    private sealed class FixedWorkspaceRootProvider : IWorkspaceRootProvider
    {
        private readonly string _workspacePath;

        public FixedWorkspaceRootProvider(string workspacePath)
        {
            _workspacePath = workspacePath;
        }

        public string GetWorkspaceRoot()
        {
            return _workspacePath;
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempWorkspace Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "NanoAgent.Tests",
                Guid.NewGuid().ToString("N"));
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
